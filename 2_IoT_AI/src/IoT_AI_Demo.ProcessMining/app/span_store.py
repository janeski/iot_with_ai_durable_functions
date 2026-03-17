"""SQLite-backed span store.

Receives raw OTLP spans and persists them. Only spans from the
'IoT_AI_Demo.Orchestrator' instrumentation scope are tracked for process mining.
All others are accepted (returns 200) but not stored.

Idempotent: INSERT OR IGNORE on span_id ensures Durable Functions replays
don't produce duplicate events in the event log.
"""

import sqlite3
import threading
from datetime import datetime, timezone
from typing import Optional

from .config import settings

_SCOPE_FILTER = "IoT_AI_Demo.Orchestrator"

_DDL = """
CREATE TABLE IF NOT EXISTS spans (
    span_id         TEXT PRIMARY KEY,
    trace_id        TEXT NOT NULL,
    parent_span_id  TEXT,
    service_name    TEXT NOT NULL,
    scope_name      TEXT NOT NULL,
    span_name       TEXT NOT NULL,
    -- nanoseconds since Unix epoch, as stored in OTLP
    start_ns        INTEGER NOT NULL,
    end_ns          INTEGER NOT NULL,
    -- XES case notion
    orchestration_id TEXT,
    -- business attributes
    device_id        TEXT,
    alarm_level      TEXT,
    alarm_value      REAL,
    adjusted_severity TEXT,
    ai_available     INTEGER,  -- 0/1/NULL
    rag_similar_count INTEGER,
    telemetry_count   INTEGER,
    span_status      TEXT,     -- OK / ERROR / UNSET
    received_at      TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_spans_orch ON spans(orchestration_id);
CREATE INDEX IF NOT EXISTS idx_spans_name ON spans(span_name);
CREATE INDEX IF NOT EXISTS idx_spans_start ON spans(start_ns);
"""

_local = threading.local()


def _conn() -> sqlite3.Connection:
    if not hasattr(_local, "conn"):
        _local.conn = sqlite3.connect(settings.sqlite_path, check_same_thread=False)
        _local.conn.row_factory = sqlite3.Row
        _local.conn.executescript(_DDL)
        _local.conn.commit()
    return _local.conn


_write_lock = threading.Lock()


def ensure_schema() -> None:
    _conn()


def _get_attr(attrs, key: str):
    for kv in attrs:
        if kv.key == key:
            vk = kv.value.WhichOneof("value")
            if vk == "string_value":
                return kv.value.string_value
            if vk == "int_value":
                return kv.value.int_value
            if vk == "double_value":
                return kv.value.double_value
            if vk == "bool_value":
                return kv.value.bool_value
    return None


def _get_service_name(resource) -> str:
    return _get_attr(resource.attributes, "service.name") or "unknown"


def _span_status(span) -> str:
    code = span.status.code
    # SpanStatusCode: 0=UNSET, 1=OK, 2=ERROR
    return {0: "UNSET", 1: "OK", 2: "ERROR"}.get(code, "UNSET")


def ingest_export_request(req) -> int:
    """Parse an ExportTraceServiceRequest and store matching spans. Returns stored count."""
    rows = []
    now = datetime.now(timezone.utc).isoformat()

    for resource_spans in req.resource_spans:
        service_name = _get_service_name(resource_spans.resource)
        for scope_spans in resource_spans.scope_spans:
            scope_name = scope_spans.scope.name
            if scope_name != _SCOPE_FILTER:
                continue
            for span in scope_spans.spans:
                span_id = span.span_id.hex()
                trace_id = span.trace_id.hex()
                parent_id = span.parent_span_id.hex() if span.parent_span_id else None

                attrs = span.attributes
                orch_id = _get_attr(attrs, "orchestration.instance_id")
                device_id = _get_attr(attrs, "device.id")
                alarm_level = _get_attr(attrs, "alarm.level")
                alarm_value = _get_attr(attrs, "alarm.value")
                adj_severity = _get_attr(attrs, "ai.adjusted_severity")
                ai_ok = _get_attr(attrs, "ai.available")
                rag_cnt = _get_attr(attrs, "rag.similar_count") or _get_attr(attrs, "rag.similar_alarms")
                tel_cnt = _get_attr(attrs, "telemetry.count")

                rows.append((
                    span_id, trace_id, parent_id,
                    service_name, scope_name, span.name,
                    span.start_time_unix_nano, span.end_time_unix_nano,
                    orch_id, device_id, alarm_level, alarm_value,
                    adj_severity,
                    int(ai_ok) if ai_ok is not None else None,
                    rag_cnt, tel_cnt,
                    _span_status(span), now,
                ))

    if not rows:
        return 0

    sql = """
        INSERT OR IGNORE INTO spans
            (span_id, trace_id, parent_span_id,
             service_name, scope_name, span_name,
             start_ns, end_ns,
             orchestration_id, device_id, alarm_level, alarm_value,
             adjusted_severity, ai_available, rag_similar_count, telemetry_count,
             span_status, received_at)
        VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
    """
    with _write_lock:
        conn = _conn()
        conn.executemany(sql, rows)
        conn.commit()

    return len(rows)


def get_stats() -> dict:
    conn = _conn()
    total = conn.execute("SELECT COUNT(*) FROM spans").fetchone()[0]
    cases = conn.execute(
        "SELECT COUNT(DISTINCT orchestration_id) FROM spans WHERE orchestration_id IS NOT NULL"
    ).fetchone()[0]
    return {"total_spans": total, "total_cases": cases}
