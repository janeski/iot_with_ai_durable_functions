FROM python:3.12-slim

# v2 — use pm4py high-level API (pm4py.discover_petri_net_inductive)
# graphviz is required by pm4py for Petri net and BPMN rendering
RUN apt-get update && apt-get install -y graphviz && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app/ ./app/

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
