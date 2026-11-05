"""PPO constructor matches the design spec."""

import gymnasium as gym
import numpy as np
import pytest
from gymnasium import spaces

pytest.importorskip("stable_baselines3")
torch = pytest.importorskip("torch")

from trainer.train import build_model


class _Tiny(gym.Env):
	"""Smallest env ``build_model`` will accept."""

	def __init__(self):
		"""Discrete buttons and a 116-float observation, same as the real env."""
		super().__init__()
		self.action_space = spaces.Discrete(54)
		self.observation_space = spaces.Box(low=-6.0, high=6.0, shape=(116,), dtype=np.float32)

	def reset(self, *, seed=None, options=None):
		"""Return a zero frame."""
		return np.zeros(116, dtype=np.float32), {}

	def step(self, action):
		"""End immediately. The constructor test never steps."""
		return np.zeros(116, dtype=np.float32), 0.0, True, False, {}


def test_hyperparameters():
	"""The MLP and PPO knobs stay on the spec values."""
	model = build_model(_Tiny(), seed=0)
	learning_rate = model.learning_rate(1.0) if callable(model.learning_rate) else model.learning_rate
	clip_range = model.clip_range(0.0) if callable(model.clip_range) else model.clip_range
	assert learning_rate == pytest.approx(3e-4)
	assert model.n_steps == 2048
	assert model.batch_size == 256
	assert model.n_epochs == 10
	assert model.gamma == pytest.approx(0.995)
	assert model.gae_lambda == pytest.approx(0.95)
	assert model.ent_coef == pytest.approx(0.01)
	assert model.vf_coef == pytest.approx(0.5)
	assert model.max_grad_norm == pytest.approx(0.5)
	assert clip_range == pytest.approx(0.2)
	assert model.policy_kwargs["net_arch"] == [128, 128]
