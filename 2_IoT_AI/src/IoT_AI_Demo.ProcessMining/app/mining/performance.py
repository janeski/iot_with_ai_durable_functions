"""Performance analysis: sojourn times and throughput.

Computes directly from the interval DataFrame (start_timestamp + time:timestamp):
- Per-activity sojourn time (mean, median, p95) in milliseconds
- Per-case throughput time (first start → last end)
- Throughput histogram

CallAiAnalysis is expected to dominate — this makes the bottleneck visible.
"""

import statistics
from datetime import timezone
from typing import Optional

import pandas as pd


def analyze(log) -> dict:
    activities = _sojourn_per_activity(log)
    throughput_ms = _case_durations_ms(log)
    tp_stats = _stats(throughput_ms)

    return {
        "activities": activities,
        "throughput": {
            "mean_ms": tp_stats["mean"],
            "median_ms": tp_stats["median"],
            "p95_ms": tp_stats["p95"],
            "min_ms": tp_stats["min"],
            "max_ms": tp_stats["max"],
            "case_count": len(throughput_ms),
            "histogram": _histogram(throughput_ms, bins=10),
        },
    }


def _sojourn_per_activity(log) -> list[dict]:
    """Compute mean sojourn time per activity from the event log."""
    from collections import defaultdict
    durations: dict[str, list[float]] = defaultdict(list)

    for trace in log:
        for event in trace:
            end = event.get("time:timestamp")
            start = event.get("start_timestamp")
            activity = event.get("concept:name", "?")
            if end is not None and start is not None:
                # Normalize timezone
                if end.tzinfo is None:
                    end = end.replace(tzinfo=timezone.utc)
                if start.tzinfo is None:
                    start = start.replace(tzinfo=timezone.utc)
                ms = (end - start).total_seconds() * 1000
                if ms >= 0:
                    durations[activity].append(ms)

    result = []
    for activity, values in durations.items():
        if values:
            result.append({
                "activity": activity,
                "mean_ms": round(statistics.mean(values), 1),
                "median_ms": round(statistics.median(values), 1),
                "count": len(values),
            })

    result.sort(key=lambda x: x["mean_ms"], reverse=True)
    return result


def _case_durations_ms(log) -> list[float]:
    durations = []
    for trace in log:
        if not trace:
            continue
        all_times = []
        for e in trace:
            end = e.get("time:timestamp")
            start = e.get("start_timestamp")
            for t in [end, start]:
                if t is not None:
                    if t.tzinfo is None:
                        t = t.replace(tzinfo=timezone.utc)
                    all_times.append(t)
        if len(all_times) >= 2:
            duration = (max(all_times) - min(all_times)).total_seconds() * 1000
            durations.append(duration)
    return durations


def _stats(values: list[float]) -> dict:
    if not values:
        return {"mean": 0, "median": 0, "p95": 0, "min": 0, "max": 0}
    sorted_v = sorted(values)
    p95_idx = max(0, int(len(sorted_v) * 0.95) - 1)
    return {
        "mean": round(statistics.mean(values), 1),
        "median": round(statistics.median(values), 1),
        "p95": round(sorted_v[p95_idx], 1),
        "min": round(min(values), 1),
        "max": round(max(values), 1),
    }


def _histogram(values: list[float], bins: int = 10) -> list[dict]:
    if not values:
        return []
    min_v, max_v = min(values), max(values)
    if min_v == max_v:
        return [{"bin_start": round(min_v, 1), "count": len(values)}]
    width = (max_v - min_v) / bins
    counts = [0] * bins
    for v in values:
        idx = min(int((v - min_v) / width), bins - 1)
        counts[idx] += 1
    return [
        {"bin_start": round(min_v + i * width, 1), "count": counts[i]}
        for i in range(bins)
    ]
