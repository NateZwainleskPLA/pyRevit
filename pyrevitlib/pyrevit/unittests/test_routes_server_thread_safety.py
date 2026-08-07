"""Tests for Routes server worker-thread diagnostics."""

import sys
import threading
import unittest

from pyrevit.routes.server import server


class _ThreadBoundStderr(object):
    def __init__(self):
        self.owner_thread = threading.current_thread().ident
        self.worker_writes = []

    def write(self, value):
        if threading.current_thread().ident != self.owner_thread:
            self.worker_writes.append(value)
            raise RuntimeError("worker thread accessed UI-backed stderr")
        return len(value)

    def flush(self):
        pass


class _BareHttpHandler(server.HttpRequestHandler):
    def __init__(self):
        self.client_address = ("127.0.0.1", 48884)


class _RecordingLogger(object):
    def __init__(self):
        self.debug_messages = []
        self.error_messages = []

    def debug(self, message, *args):
        self.debug_messages.append(message % args if args else message)

    def error(self, message, *args):
        self.error_messages.append(message % args if args else message)


class RoutesServerThreadSafetyTests(unittest.TestCase):
    """Verify Routes workers keep diagnostics away from UI-backed streams."""

    def setUp(self):
        """Install output doubles that reject worker-thread writes."""
        self.original_stderr = sys.stderr
        self.original_logger = server.mlogger
        self.stderr = _ThreadBoundStderr()
        self.logger = _RecordingLogger()
        sys.stderr = self.stderr
        server.mlogger = self.logger

    def tearDown(self):
        """Restore the process-wide streams and logger after each test."""
        sys.stderr = self.original_stderr
        server.mlogger = self.original_logger

    def _run_on_worker(self, callback):
        errors = []

        def run_callback():
            try:
                callback()
            except Exception as error:
                errors.append(error)

        worker = threading.Thread(target=run_callback)
        worker.start()
        worker.join(2)
        self.assertFalse(worker.is_alive(), "worker thread did not finish")
        return errors

    def test_request_logging_does_not_touch_ui_stderr_from_worker(self):
        """Route access logs must use the thread-safe logger."""
        http_handler = _BareHttpHandler()

        errors = self._run_on_worker(
            lambda: http_handler.log_message('"GET /revit_mcp/status" 200 -')
        )

        self.assertEqual([], errors)
        self.assertEqual([], self.stderr.worker_writes)
        self.assertEqual(1, len(self.logger.debug_messages))
        self.assertIn("GET /revit_mcp/status", self.logger.debug_messages[0])

    def test_worker_errors_do_not_touch_ui_stderr(self):
        """Uncaught request errors must use the thread-safe logger."""
        http_server = object.__new__(server.ThreadedHttpServer)

        def report_worker_error():
            try:
                raise RuntimeError("simulated client disconnect")
            except RuntimeError:
                http_server.handle_error(None, ("127.0.0.1", 48884))

        errors = self._run_on_worker(report_worker_error)

        self.assertEqual([], errors)
        self.assertEqual([], self.stderr.worker_writes)
        self.assertEqual(1, len(self.logger.error_messages))
        self.assertIn("simulated client disconnect", self.logger.error_messages[0])


if __name__ == "__main__":
    unittest.main()
