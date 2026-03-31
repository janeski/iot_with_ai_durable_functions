"""Process discovery using Inductive Miner with noise filtering (IMf).

Uses pm4py's stable high-level API (pm4py.discover_petri_net_inductive) which
correctly handles the parallel fan-out (Task.WhenAll) and the AI-vs-fallback branch.
"""

import os
import tempfile

import pm4py


def discover(log, noise_threshold: float = 0.2) -> dict:
    """Discover a Petri net via IMf and return SVG + model stats."""
    net, im, fm = pm4py.discover_petri_net_inductive(log, noise_threshold=noise_threshold)
    svg = _render_svg(net, im, fm)

    places = len(net.places)
    transitions = len([t for t in net.transitions if t.label is not None])
    silent = len([t for t in net.transitions if t.label is None])

    return {
        "svg": svg,
        "places": places,
        "transitions": transitions,
        "silent_transitions": silent,
        "noise_threshold": noise_threshold,
    }


def get_net(log, noise_threshold: float = 0.2):
    """Return (net, im, fm) for use by conformance checking."""
    return pm4py.discover_petri_net_inductive(log, noise_threshold=noise_threshold)


def _render_svg(net, im, fm) -> str:
    from pm4py.visualization.petri_net import visualizer as pn_vis

    with tempfile.NamedTemporaryFile(suffix=".svg", delete=False) as f:
        path = f.name

    try:
        gviz = pn_vis.apply(net, im, fm, parameters={"format": "svg"})
        pn_vis.save(gviz, path)
        with open(path, "r", encoding="utf-8") as f:
            return f.read()
    finally:
        if os.path.exists(path):
            os.unlink(path)
