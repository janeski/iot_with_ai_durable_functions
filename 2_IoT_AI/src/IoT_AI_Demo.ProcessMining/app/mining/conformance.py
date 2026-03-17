"""Conformance checking via token-based replay."""

import pm4py
from pm4py.algo.conformance.tokenreplay import algorithm as token_replay


def check(log, net, im, fm) -> dict:
    replayed = token_replay.apply(log, net, im, fm)

    fitness_scores = [t["trace_fitness"] for t in replayed]
    avg_fitness = sum(fitness_scores) / len(fitness_scores) if fitness_scores else 0.0

    per_case = []
    for i, t in enumerate(replayed):
        # trace_id may not always be set; fall back to index
        case_id = t.get("trace_id") or str(i)
        per_case.append({
            "case_id": case_id,
            "fitness": round(t["trace_fitness"], 4),
            "missing_tokens": t.get("missing_tokens", 0),
            "remaining_tokens": t.get("remaining_tokens", 0),
            "is_fit": t["trace_fitness"] >= 0.8,
        })

    per_case.sort(key=lambda x: x["fitness"])

    fit_count = sum(1 for c in per_case if c["is_fit"])
    return {
        "average_fitness": round(avg_fitness, 4),
        "fit_cases": fit_count,
        "total_cases": len(per_case),
        "fit_ratio": round(fit_count / len(per_case), 4) if per_case else 0.0,
        "per_case": per_case,
    }
