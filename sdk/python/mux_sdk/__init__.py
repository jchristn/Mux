"""@mux/sdk (Python) -- a thin driver for the mux CLI.

Spawns ``mux print --output-format jsonl`` and returns typed results. See :class:`Mux`.
"""

from .client import Mux, MuxOptions, RunResult, Thread, build_print_args

__all__ = ["Mux", "MuxOptions", "RunResult", "Thread", "build_print_args"]

__version__ = "0.7.0"
