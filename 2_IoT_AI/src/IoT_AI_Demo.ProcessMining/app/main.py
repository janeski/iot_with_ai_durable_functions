"""Process Mining Service — FastAPI application.

Endpoints:
  POST /v1/traces          — OTLP/HTTP protobuf receiver (called by .NET services)
  GET  /api/status         — span store stats + readiness
  GET  /api/filters        — distinct device IDs and alarm levels for UI
  GET  /api/discovery      — IMf Petri net as SVG + model stats
  GET  /api/conformance    — token replay fitness per case
  GET  /api/performance    — sojourn times + throughput histogram
  GET  /api/variants       — variant analysis + DFG
  GET  /api/export/xes     — download XES event log
  GET  /api/export/bpmn    — download BPMN 2.0 XML
  GET  /dashboard          — single-page web UI
"""

import os
import time
import hashlib
import json
from contextlib import asynccontextmanager

from fastapi import FastAPI, Query, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse
from fastapi.staticfiles import StaticFiles

from .config import settings
from .span_store import ensure_schema, get_stats, ingest_export_request
from .event_log import build_event_log, get_distinct_values
from .mining import conformance, discovery, export, performance, variants

# Simple in-memory cache: key → (result, timestamp)
_cache: dict = {}
_CACHE_TTL = 30  # seconds


def _cache_key(**kwargs) -> str:
    return hashlib.md5(json.dumps(kwargs, sort_keys=True).encode()).hexdigest()


def _cached(key: str):
    entry = _cache.get(key)
    if entry and (time.monotonic() - entry[1]) < _CACHE_TTL:
        return entry[0]
    return None


def _store(key: str, value):
    _cache[key] = (value, time.monotonic())
    return value


@asynccontextmanager
async def lifespan(app: FastAPI):
    os.makedirs(os.path.dirname(settings.sqlite_path), exist_ok=True)
    ensure_schema()
    yield


app = FastAPI(
    title="IoT Process Mining",
    description="pm4py-powered process mining over IoT alarm analysis OTLP traces",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ── OTLP receiver ────────────────────────────────────────────────────────────

@app.post("/v1/traces", status_code=200)
async def receive_traces(request: Request):
    """Accept OTLP/HTTP protobuf trace export from .NET services."""
    body = await request.body()
    try:
        from opentelemetry.proto.collector.trace.v1.trace_service_pb2 import (
            ExportTraceServiceRequest,
        )
        req = ExportTraceServiceRequest()
        req.ParseFromString(body)
        stored = ingest_export_request(req)
    except Exception:
        # Always return 200 to avoid retry storms from OTLP exporters
        pass
    return Response(status_code=200)


# ── API endpoints ─────────────────────────────────────────────────────────────

@app.get("/api/status")
def status():
    stats = get_stats()
    return {
        **stats,
        "ready": stats["total_cases"] >= settings.min_cases_for_mining,
        "min_cases_required": settings.min_cases_for_mining,
    }


@app.get("/api/filters")
def filters():
    return get_distinct_values()


def _get_log(
    device_id: str | None,
    alarm_level: str | None,
    from_ts: str | None,
    to_ts: str | None,
):
    log = build_event_log(device_id, alarm_level, from_ts, to_ts)
    return log


@app.get("/api/discovery")
def api_discovery(
    device_id: str | None = Query(None),
    alarm_level: str | None = Query(None),
    from_ts: str | None = Query(None),
    to_ts: str | None = Query(None),
    noise_threshold: float = Query(0.2, ge=0.0, le=0.9),
):
    key = _cache_key(ep="discovery", device_id=device_id, alarm_level=alarm_level,
                     from_ts=from_ts, to_ts=to_ts, noise_threshold=noise_threshold)
    if (hit := _cached(key)) is not None:
        return hit
    log = _get_log(device_id, alarm_level, from_ts, to_ts)
    if log is None:
        return {"error": "insufficient_data", "message": "Need at least 3 complete cases to discover a process model."}
    return _store(key, discovery.discover(log, noise_threshold))


@app.get("/api/conformance")
def api_conformance(
    device_id: str | None = Query(None),
    alarm_level: str | None = Query(None),
    from_ts: str | None = Query(None),
    to_ts: str | None = Query(None),
    noise_threshold: float = Query(0.2, ge=0.0, le=0.9),
):
    key = _cache_key(ep="conformance", device_id=device_id, alarm_level=alarm_level,
                     from_ts=from_ts, to_ts=to_ts, noise_threshold=noise_threshold)
    if (hit := _cached(key)) is not None:
        return hit
    log = _get_log(device_id, alarm_level, from_ts, to_ts)
    if log is None:
        return {"error": "insufficient_data", "message": "Need at least 3 complete cases."}
    net, im, fm = discovery.get_net(log, noise_threshold)
    return _store(key, conformance.check(log, net, im, fm))


@app.get("/api/performance")
def api_performance(
    device_id: str | None = Query(None),
    alarm_level: str | None = Query(None),
    from_ts: str | None = Query(None),
    to_ts: str | None = Query(None),
):
    key = _cache_key(ep="performance", device_id=device_id, alarm_level=alarm_level,
                     from_ts=from_ts, to_ts=to_ts)
    if (hit := _cached(key)) is not None:
        return hit
    log = _get_log(device_id, alarm_level, from_ts, to_ts)
    if log is None:
        return {"error": "insufficient_data", "message": "Need at least 3 complete cases."}
    return _store(key, performance.analyze(log))


@app.get("/api/variants")
def api_variants(
    device_id: str | None = Query(None),
    alarm_level: str | None = Query(None),
    from_ts: str | None = Query(None),
    to_ts: str | None = Query(None),
    top_n: int = Query(10, ge=1, le=50),
):
    key = _cache_key(ep="variants", device_id=device_id, alarm_level=alarm_level,
                     from_ts=from_ts, to_ts=to_ts, top_n=top_n)
    if (hit := _cached(key)) is not None:
        return hit
    log = _get_log(device_id, alarm_level, from_ts, to_ts)
    if log is None:
        return {"error": "insufficient_data", "message": "Need at least 3 complete cases."}
    return _store(key, variants.analyze(log, top_n))


@app.get("/api/export/xes")
def api_export_xes(
    device_id: str | None = Query(None),
    alarm_level: str | None = Query(None),
    from_ts: str | None = Query(None),
    to_ts: str | None = Query(None),
):
    log = _get_log(device_id, alarm_level, from_ts, to_ts)
    if log is None:
        return Response(status_code=404, content="Insufficient data for XES export.")
    xes_bytes = export.export_xes(log)
    return Response(
        content=xes_bytes,
        media_type="application/xml",
        headers={"Content-Disposition": "attachment; filename=alarm_analysis_process.xes"},
    )


@app.get("/api/export/bpmn")
def api_export_bpmn(
    device_id: str | None = Query(None),
    alarm_level: str | None = Query(None),
    from_ts: str | None = Query(None),
    to_ts: str | None = Query(None),
    noise_threshold: float = Query(0.2, ge=0.0, le=0.9),
):
    log = _get_log(device_id, alarm_level, from_ts, to_ts)
    if log is None:
        return Response(status_code=404, content="Insufficient data for BPMN export.")
    bpmn_xml = export.export_bpmn(log, noise_threshold)
    return Response(
        content=bpmn_xml,
        media_type="application/xml",
        headers={"Content-Disposition": "attachment; filename=alarm_analysis_process.bpmn"},
    )


# ── Dashboard UI ──────────────────────────────────────────────────────────────

_STATIC_DIR = os.path.join(os.path.dirname(__file__), "static")


@app.get("/dashboard", response_class=HTMLResponse)
def dashboard():
    with open(os.path.join(_STATIC_DIR, "index.html"), "r", encoding="utf-8") as f:
        return HTMLResponse(content=f.read())


@app.get("/")
def root():
    from fastapi.responses import RedirectResponse
    return RedirectResponse(url="/dashboard")
