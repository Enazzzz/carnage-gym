"""PPO training loop. The opponent is the Roosevelt already in the match."""

import argparse
import csv
from pathlib import Path

from trainer.curriculum import Curriculum
from trainer.env import CarnageEnv


def build_model(env, seed=0):
	"""Construct PPO with the hyperparameters locked by the design spec."""
	import torch
	from stable_baselines3 import PPO

	device = "cuda" if torch.cuda.is_available() else "cpu"
	return PPO(
		"MlpPolicy",
		env,
		learning_rate=3e-4,
		n_steps=2048,
		batch_size=256,
		n_epochs=10,
		gamma=0.995,
		gae_lambda=0.95,
		clip_range=0.2,
		ent_coef=0.01,
		vf_coef=0.5,
		max_grad_norm=0.5,
		seed=seed,
		policy_kwargs={"net_arch": [128, 128]},
		device=device,
		verbose=0,
	)


def run_eval(model, env, episodes=10):
	"""Play deterministic rounds at time scale 1 and return the win rate."""
	saved = env.time_scale
	env.time_scale = 1.0
	wins = 0
	try:
		for _episode in range(episodes):
			obs, _info = env.reset()
			done = False
			info = {}
			while not done:
				action, _states = model.predict(obs, deterministic=True)
				obs, _reward, terminated, truncated, info = env.step(int(action))
				done = bool(terminated or truncated)
			if info.get("winner") == "agent":
				wins += 1
	finally:
		env.time_scale = saved
	return wins / float(episodes)


def _append_progress(path, row) -> None:
	"""Append one CSV row, writing the header when the file is new."""
	path.parent.mkdir(parents=True, exist_ok=True)
	write_header = not path.exists()
	with path.open("a", newline="", encoding="utf-8") as handle:
		writer = csv.DictWriter(
			handle,
			fieldnames=[
				"timestep",
				"cpu_level",
				"win_rate_30",
				"eval_win_rate",
				"ep_reward_mean",
				"time_scale",
			],
		)
		if write_header:
			writer.writeheader()
		writer.writerow(row)


def main(argv=None) -> None:
	"""Connect to a running game and train."""
	parser = argparse.ArgumentParser(description="Train against the Roosevelt already in the local match.")
	parser.add_argument("--timesteps", type=int, default=2_000_000)
	parser.add_argument("--cpu-start", type=int, default=0)
	parser.add_argument("--time-scale", type=float, default=3.0)
	parser.add_argument("--host", default="127.0.0.1")
	parser.add_argument("--port", type=int, default=47831)
	parser.add_argument("--seed", type=int, default=0)
	args = parser.parse_args(argv)
	env = CarnageEnv(
		host=args.host,
		port=args.port,
		cpu_level=args.cpu_start,
		time_scale=args.time_scale,
		connect_timeout=90.0,
	)
	try:
		_obs, info = env.reset()
		minimum = int(info.get("cpu_min", args.cpu_start))
		maximum = int(info.get("cpu_max", args.cpu_start))
		env.cpu_level = min(max(args.cpu_start, minimum), maximum)
		# The helper below rebuilds the curriculum from the probed bounds.
		_train_with_bounds(env, args.timesteps, args.seed, minimum, maximum)
	finally:
		env.close()


def _train_with_bounds(env, timesteps, seed, minimum, maximum):
	"""Train using probed CPU bounds instead of a single stuck level."""
	from stable_baselines3.common.callbacks import BaseCallback

	model = build_model(env, seed=seed)
	curriculum = Curriculum(env.cpu_level, minimum, maximum)
	checkpoint_dir = Path("checkpoints")
	checkpoint_dir.mkdir(parents=True, exist_ok=True)
	log_path = Path("logs/progress.csv")

	class _Window(BaseCallback):
		"""Feed finished training rounds into the curriculum."""

		def __init__(self):
			"""Attach the shared curriculum."""
			super().__init__()

		def _on_step(self) -> bool:
			"""Record winners from training steps."""
			infos = self.locals.get("infos") or []
			dones = self.locals.get("dones") or []
			for done, info in zip(dones, infos):
				if done:
					curriculum.record(info.get("winner"))
				if info.get("possible_stall"):
					print("possible_stall")
			return True

	completed = 0
	window = _Window()
	while completed < timesteps:
		chunk = min(10000, timesteps - completed)
		model.learn(total_timesteps=chunk, callback=window, reset_num_timesteps=False)
		completed += chunk
		env.cpu_level = curriculum.level
		eval_rate = run_eval(model, env, episodes=10)
		rewards = model.ep_info_buffer
		mean_reward = ""
		if rewards:
			mean_reward = sum(item["r"] for item in rewards) / len(rewards)
		_append_progress(
			log_path,
			{
				"timestep": completed,
				"cpu_level": curriculum.level,
				"win_rate_30": curriculum.win_rate_30,
				"eval_win_rate": eval_rate,
				"ep_reward_mean": mean_reward,
				"time_scale": env.time_scale,
			},
		)
		if completed % 20480 == 0 or completed >= timesteps:
			model.save(str(checkpoint_dir / "lincoln_ppo"))
	return model


if __name__ == "__main__":
	main()
