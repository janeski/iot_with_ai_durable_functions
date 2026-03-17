"""XES and BPMN export using pm4py's high-level API."""

import os
import tempfile

import pm4py


def export_xes(log) -> bytes:
    with tempfile.NamedTemporaryFile(suffix=".xes", delete=False) as f:
        path = f.name
    try:
        pm4py.write_xes(log, path)
        with open(path, "rb") as f:
            return f.read()
    finally:
        if os.path.exists(path):
            os.unlink(path)


def export_bpmn(log, noise_threshold: float = 0.2) -> str:
    net, im, fm = pm4py.discover_petri_net_inductive(log, noise_threshold=noise_threshold)
    bpmn_graph = pm4py.convert_to_bpmn(net, im, fm)

    with tempfile.NamedTemporaryFile(suffix=".bpmn", delete=False) as f:
        path = f.name
    try:
        pm4py.write_bpmn(bpmn_graph, path)
        with open(path, "r", encoding="utf-8") as f:
            return f.read()
    finally:
        if os.path.exists(path):
            os.unlink(path)
