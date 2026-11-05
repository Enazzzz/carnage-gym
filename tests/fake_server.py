"""In-process stand-in for the BepInEx socket."""

import json
import socket
import threading


class FakeMatch:
	"""Serve scripted NDJSON responses on an ephemeral localhost port."""

	def __init__(self, script, default=None):
		"""Queue ``script`` responses. Extra requests use ``default`` when set."""
		self.script = list(script)
		self.default = default
		self.requests = []
		self.port = None
		self._thread = None
		self._stop = False

	def __enter__(self):
		"""Bind and start the accept thread."""
		sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
		sock.bind(("127.0.0.1", 0))
		self.port = sock.getsockname()[1]
		sock.listen(1)
		self._thread = threading.Thread(target=self._serve, args=(sock,), daemon=True)
		self._thread.start()
		return self

	def __exit__(self, exc_type, exc, tb):
		"""Stop the accept loop."""
		self._stop = True
		try:
			socket.create_connection(("127.0.0.1", self.port), timeout=0.2).close()
		except OSError:
			pass
		if self._thread is not None:
			self._thread.join(timeout=2)
		return False

	def _serve(self, sock) -> None:
		"""Accept one client and answer line by line."""
		sock.settimeout(0.2)
		conn = None
		try:
			while conn is None and not self._stop:
				try:
					conn, _addr = sock.accept()
				except socket.timeout:
					continue
			if conn is None:
				return
			conn.settimeout(0.5)
			buf = b""
			while not self._stop:
				try:
					chunk = conn.recv(4096)
				except socket.timeout:
					continue
				if not chunk:
					break
				buf += chunk
				while b"\n" in buf:
					line, buf = buf.split(b"\n", 1)
					if not line.strip():
						continue
					request = json.loads(line.decode("utf-8"))
					self.requests.append(request)
					if request.get("cmd") == "ping":
						body = {"ok": True}
					elif self.script:
						body = self.script.pop(0)
					elif self.default is not None:
						body = self.default
					else:
						body = {"ok": False, "error": "script_empty", "obs": [0.0] * 29, "reward": 0.0, "done": True, "info": {}}
					conn.sendall((json.dumps(body) + "\n").encode("utf-8"))
		finally:
			if conn is not None:
				conn.close()
			sock.close()
