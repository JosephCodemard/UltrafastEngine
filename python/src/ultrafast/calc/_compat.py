"""Shared helpers of the calculators: keyword renames with a deprecation warning, and the engine-error conversion.

``renamed_kwargs({"duration": "duration_s"})`` keeps an old keyword working: it is mapped to the new name and a
``DeprecationWarning`` is issued (the project's rule for renamed keys).
``engine_call(fn)`` runs a call into the engine and re-raises its errors (``UltrafastError``) as ``ValueError`` with the
engine's message, so every calculator reports bad arguments the same way.
"""
from __future__ import annotations

import functools
import warnings
from typing import Any, Callable, Dict, TypeVar

F = TypeVar("F", bound=Callable[..., Any])


def renamed_kwargs(mapping: Dict[str, str]) -> Callable[[F], F]:
    """Decorator: accept the old keyword names of ``mapping`` (old -> new) with a ``DeprecationWarning``."""
    def deco(fn: F) -> F:
        @functools.wraps(fn)
        def wrapper(*args: Any, **kwargs: Any):
            for old, new in mapping.items():
                if old in kwargs:
                    if new in kwargs:
                        raise TypeError(f"{fn.__name__}() got both '{old}' (deprecated) and '{new}'")
                    warnings.warn(f"{fn.__name__}(): keyword '{old}' is renamed '{new}' (the unit is in the name); the old name still works",
                                  DeprecationWarning, stacklevel=2)
                    kwargs[new] = kwargs.pop(old)
            return fn(*args, **kwargs)
        return wrapper  # type: ignore[return-value]
    return deco


def engine_call(fn: Callable[[], Any]) -> Any:
    """Run ``fn()``; an engine error becomes ``ValueError(message)`` (argument problems) while a missing engine stays as it is."""
    from ..core import UltrafastError
    try:
        return fn()
    except UltrafastError as e:
        raise ValueError(str(e).split("] ", 1)[-1]) from e
