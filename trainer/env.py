"""Gymnasium adapter for the CarnageGym socket."""

import json
import socket
import time

import gymnasium as gym
import numpy as np
from gymnasium import spaces

from trainer.actions import N_ACTIONS
from trainer.reward import reward_from_transition
from trainer.stack import OBS_LEN, ObsStack


LAUNCH_MESSAGE = (
	"Open a local match against Roosevelt, with CarnageGym running, before training."
)


class GameConnectionError(RuntimeError):
	"""The game socket could not be used."""


class CarnageEnv(gym.Env):
	"""One local training match. Reward is computed here, not in the plugin."""

	metadata = {"render_modes": []}

	def __init__(
		self,
		host="127.0.0.1",
		port=47831,
		cpu_level=0,
		time_scale=3.0,
		max_resets=3,
		connect_timeout=30.0,
	):
		"""Store the socket target. The connection opens on the first reset."""
		super().__init__()
		self.host = host
		self.port = int(port)
		self.cpu_level = int(cpu_level)
		self.time_scale = float(time_scale)
		self.max_resets = int(max_resets)
		self.connect_timeout = float(connect_timeout)
		self.action_space = spaces.Discrete(N_ACTIONS)
		self.observation_space = spaces.Box(
			low=-6.0, high=6.0, shape=(116,), dtype=np.float32
		)
		self._stack = ObsStack()
		self._sock = None
		self._buffer = b""
		self._previous = None
		self._still = 0
		self._stall_latched = False
		self._scale_dropped = False

	def reset(self, *, seed=None, options=None):
		"""Restart the round. Failed resets are retried up to ``max_resets``."""
		super().reset(seed=seed)
		self._ensure_connection()
		if self._stall_latched and not self._scale_dropped:
			self.time_scale = 1.0
			self._scale_dropped = True
		last_error = "reset failed"
		for _attempt in range(self.max_resets):
			response = self._request(
				{
					"cmd": "reset",
					"cpu_level": int(self.cpu_level),
					"time_scale": float(self.time_scale),
				}
			)
			if response.get("ok"):
				obs = self._raw_obs(response)
				self._previous = None
				self._still = 0
				info = dict(response.get("info") or {})
				return np.asarray(self._stack.reset(obs), dtype=np.float32), info
			last_error = str(response.get("error") or last_error)
		raise GameConnectionError(last_error)

	def step(self, action):
		"""Hold one discrete action for a decision and score the transition."""
		self._ensure_connection()
		response = self._request({"cmd": "step", "action": int(action)})
		info = dict(response.get("info") or {})
		obs = self._raw_obs(response)
		done = bool(response.get("done")) or not response.get("ok")
		winner = info.get("winner")
		if done and winner is None:
			winner = "draw"
			info["winner"] = winner
		self._note_stall(obs, info)
		reward = reward_from_transition(
			self._previous, obs, done, winner if done else None
		)
		self._previous = list(obs)
		vector = np.asarray(self._stack.push(obs), dtype=np.float32)
		return vector, float(reward), bool(done), False, info

	def close(self):
		"""Close the socket if it is open."""
		if self._sock is not None:
			try:
				self._sock.close()
			except OSError:
				pass
			self._sock = None

	def _ensure_connection(self) -> None:
		"""Connect, retrying until ``connect_timeout`` seconds have passed."""
		if self._sock is not None:
			return
		deadline = time.time() + self.connect_timeout
		while True:
			remaining = deadline - time.time()
			if remaining <= 0:
				raise GameConnectionError(LAUNCH_MESSAGE)
			try:
				sock = socket.create_connection((self.host, self.port), timeout=min(2.0, remaining))
				sock.settimeout(5)
				self._sock = sock
				self._buffer = b""
				return
			except OSError:
				if time.time() >= deadline:
					raise GameConnectionError(LAUNCH_MESSAGE)
				time.sleep(min(0.05, max(0.0, deadline - time.time())))

	def _request(self, payload) -> dict:
		"""Send one JSON line and read one JSON line."""
		raw = (json.dumps(payload) + "\n").encode("utf-8")
		try:
			self._sock.sendall(raw)
			line = self._read_line()
		except OSError as exc:
			self.close()
			raise GameConnectionError(LAUNCH_MESSAGE) from exc
		return json.loads(line)

	def _read_line(self) -> str:
		"""Read until a newline from the plugin."""
		while b"\n" not in self._buffer:
			chunk = self._sock.recv(4096)
			if not chunk:
				raise OSError("socket closed")
			self._buffer += chunk
		line, self._buffer = self._buffer.split(b"\n", 1)
		return line.decode("utf-8")

	def _raw_obs(self, response) -> list:
		"""Return 29 floats, using zeros when the plugin omitted a usable vector."""
		obs = response.get("obs")
		if not isinstance(obs, list) or len(obs) != OBS_LEN:
			return [0.0] * OBS_LEN
		clean = []
		for value in obs:
			number = float(value)
			if number != number or number in (float("inf"), float("-inf")):
				number = 0.0
			clean.append(number)
		return clean

	def _note_stall(self, obs, info) -> None:
		"""Latch a stall after 120 motionless decisions. The next reset drops to 1x."""
		if self._previous is not None:
			same = True
			for index in (0, 1, 2, 3, 10, 11):
				if abs(self._previous[index] - obs[index]) > 1e-6:
					same = False
					break
			self._still = self._still + 1 if same else 0
		if self._still >= 120 and not self._stall_latched:
			self._stall_latched = True
			info["possible_stall"] = True
