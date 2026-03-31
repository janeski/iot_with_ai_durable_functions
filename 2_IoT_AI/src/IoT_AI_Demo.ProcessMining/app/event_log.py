"""Convert stored spans to a pm4py interval EventLog.

Each orchestration_id is a case. Each span is an event with:
  - concept:name  = span_name
  - time:timestamp = end time (completion)
  - start_timestamp = start time
  - org:resource   = device_id
  - alarm:level    = alarm_level
  - alarm:severity = adjusted_severity

Spans without orchestration_id are excluded.
Cases with fewer than 2 events are excluded (not enough to mine).
"""

import sqlite3
from datetime import datetime, timezone
from typing import Optional

import pandas as pd
import pm4py

from .config import settings
from .span_store import _conn


def _ns_to_dt(ns: int) -> datetime:
    return datetime.fromtimestamp(ns / 1e9, tz=timezone.utc)


def load_dataframe(
    device_id: Optional[str] = None,
    alarm_level: Optional[str] = None,
    from_ts: Optional[str] = None,
    to_ts: Optional[str] = None,
) -> pd.DataFrame:
    """Load spans into a DataFrame ready for pm4py formatting."""
    conn = _conn()

    conditions = ["orchestration_id IS NOT NULL"]
    params: list = []

    if device_id:
        conditions.append("device_id = ?")
        params.append(device_id)
    if alarm_level:
        conditions.append("alarm_level = ?")
        params.append(alarm_level)
    if from_ts:
        # Convert ISO string to nanoseconds
        dt = datetime.fromisoformat(from_ts.replace("Z", "+00:00"))
        conditions.append("start_ns >= ?")
        params.append(int(dt.timestamp() * 1e9))
    if to_ts:
        dt = datetime.fromisoformat(to_ts.replace("Z", "+00:00"))
        conditions.append("end_ns <= ?")
        params.append(int(dt.timestamp() * 1e9))

    where = " AND ".join(conditions)
    sql = f"""
        SELECT
            orchestration_id,
            span_name,
            start_ns,
            end_ns,
            device_id,
            alarm_level,
            adjusted_severity,
            ai_available,
            span_status
        FROM spans
        WHERE {where}
        ORDER BY start_ns
    """
    rows = conn.execute(sql, params).fetchall()

    if not rows:
        return pd.DataFrame()

    df = pd.DataFrame(
        rows,
        columns=[
            "case:concept:name",
            "concept:name",
            "start_ns",
            "end_ns",
            "org:resource",
            "alarm:level",
            "alarm:severity",
            "ai:available",
            "span:status",
        ],
    )

    df["time:timestamp"] = df["end_ns"].apply(_ns_to_dt)
    df["start_timestamp"] = df["start_ns"].apply(_ns_to_dt)
    df.drop(columns=["start_ns", "end_ns"], inplace=True)

    # Remove cases with fewer than 2 events — not enough to discover flow
    case_counts = df["case:concept:name"].value_counts()
    valid_cases = case_counts[case_counts >= 2].index
    df = df[df["case:concept:name"].isin(valid_cases)]

    return df


def build_event_log(
    device_id: Optional[str] = None,
    alarm_level: Optional[str] = None,
    from_ts: Optional[str] = None,
    to_ts: Optional[str] = None,
) -> Optional[object]:
    """Return a pm4py EventLog, or None if insufficient data."""
    df = load_dataframe(device_id, alarm_level, from_ts, to_ts)
    if df.empty or df["case:concept:name"].nunique() < 1:
        return None

    log = pm4py.format_dataframe(
        df,
        case_id="case:concept:name",
        activity_key="concept:name",
        timestamp_key="time:timestamp",
        start_timestamp_key="start_timestamp",
    )
    return pm4py.convert_to_event_log(log)


def get_distinct_values() -> dict:
    """Return distinct device IDs and alarm levels for filter UI."""
    conn = _conn()
    devices = [r[0] for r in conn.execute(
        "SELECT DISTINCT device_id FROM spans WHERE device_id IS NOT NULL ORDER BY device_id"
    ).fetchall()]
    levels = [r[0] for r in conn.execute(
        "SELECT DISTINCT alarm_level FROM spans WHERE alarm_level IS NOT NULL ORDER BY alarm_level"
    ).fetchall()]
    return {"devices": devices, "alarm_levels": levels}
