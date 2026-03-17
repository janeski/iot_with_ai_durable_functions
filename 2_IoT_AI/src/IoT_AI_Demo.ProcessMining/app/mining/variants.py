"""Variant analysis using pm4py's high-level API."""

import pm4py


def analyze(log, top_n: int = 10) -> dict:
    # High-level variant API — stable across pm4py 2.x
    variants_dict = pm4py.get_variants(log)

    variant_list = []
    for sequence, traces in variants_dict.items():
        if isinstance(sequence, tuple):
            activities = list(sequence)
        else:
            activities = [str(sequence)]

        variant_list.append({
            "sequence": activities,
            "case_count": len(traces),
            "case_ids": [str(t.attributes.get("concept:name", "?")) for t in traces],
        })

    variant_list.sort(key=lambda x: x["case_count"], reverse=True)

    # DFG via high-level API
    dfg, start_acts, end_acts = pm4py.discover_dfg(log)
    dfg_data = [
        {"from": str(k[0]), "to": str(k[1]), "count": v}
        for k, v in dfg.items()
    ]
    dfg_data.sort(key=lambda x: x["count"], reverse=True)

    return {
        "total_variants": len(variant_list),
        "top_variants": variant_list[:top_n],
        "dfg": dfg_data[:50],
        "start_activities": {str(k): v for k, v in start_acts.items()},
        "end_activities": {str(k): v for k, v in end_acts.items()},
    }
