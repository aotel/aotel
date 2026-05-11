# AOTel

AOTel: A Native AOT First-Mile Ingestion Layer for OpenTelemetry

[![Continuous Integration](https://github.com/aotel/aotel/actions/workflows/ci.yml/badge.svg)](https://github.com/aotel/aotel/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

AOTel is an ultra-lightweight telemetry edge proxy designed to sit as a DaemonSet on every node in your Kubernetes cluster. By leveraging the extreme performance and memory-safety of .NET 10 Native AOT, AOTel provides a robust buffer between your high-throughput microservices and your central observability backend.

---

## ⚡ The AOTel Architecture: Tiered Ingestion
To ensure deterministic performance under bursty telemetry loads, AOTel implements a tiered approach. Use your central Collector for the complex "brain" work, and use AOTel DaemonSets for the high-speed "muscle" at the edge.

```
[ Application Pods ]
         │
         │  OTLP/HTTP (Localhost/Host IP)
         ▼
[ Node-local AOTel DaemonSet ]  <-- Allocation-Free OTLP Parsing, Bounded Buffer
         │
         │  OTLP/HTTP (Batched & Validated)
         ▼
[ Central OTel Collector ]      <-- Complex Routing, Sampling, Plugins
         │
         │  gRPC/HTTP
         ▼
[ Observability Backend ]       <-- Jaeger, Honeycomb, Datadog
```

<sub>**Note on Protocols:** AOTel currently focuses on OTLP/HTTP (Protobuf) to provide the lowest possible entry barrier and the most stable memory profile for edge ingestion. OTLP/gRPC support is planned for the v0.2 roadmap.</sub>

---

## 🚀 The Operational Story: Why AOTel?

Modern cloud-native applications generate massive amounts of telemetry (traces, metrics, and logs). Sending this raw Protobuf data directly to a centralized OpenTelemetry Collector or observability backend introduces significant friction:

1. **Cross-Node Latency & Cost**: Applications sending telemetry out of their local node incur network hops, cross-AZ data transfer charges, and potential network saturation.
2. **Application GC Pressure**: Telemetry export pipelines can introduce additional CPU, memory, and retry overhead inside application processes—especially under high throughput or degraded network conditions.
3. **Heavy Central Collectors**: Centralized collectors often require gigabytes of memory to handle cluster-wide ingestion spikes.

**AOTel flips the architecture.** Deployed as a Kubernetes DaemonSet, AOTel is designed around a zero-allocation parsing pipeline. Application pods send telemetry directly to their local node's IP (`HOST_IP:4318`). AOTel uses bounded in-memory buffering to absorb bursts locally before efficiently forwarding telemetry upstream.

Your application pods are relieved of memory pressure, and cross-node telemetry traffic is drastically minimized.

---

## When AOTel may not be necessary

AOTel is most beneficial in:
- high-throughput Kubernetes clusters
- noisy multi-tenant environments
- latency-sensitive workloads
- clusters with heavy telemetry fan-in

Smaller deployments may be well-served by a standard OTel Collector alone.

---

## ⚡ Performance & Resource Footprint

AOTel is engineered with careful attention to memory layout and execution speed. Using strict `ref struct` state machines and `ReadOnlySpan<byte>` parsers, the core ingestion pipeline is allocation-free on the hot path.

### Core Benchmarks

| Metric | Value | Description |
| :--- | :--- | :--- |
| **Allocations (Gen 0/1/2)** | **0 Bytes** | The steady-state ingestion path avoids managed heap allocations during OTLP parsing and traversal. |
| **Deployment Footprint** | **16.7 MB** | Fully statically linked Native AOT Linux binary deployed in a `scratch` container. |
| **Idle Memory Usage** | **~ 15 MiB** | Ultra-low base resident set size (RSS), leaving maximum room for your business workloads. |

*Benchmarks are enforced automatically in CI using BenchmarkDotNet on .NET 10.*

---

## 🤝 How it fits with the OTel Collector
AOTel is designed to complement, not replace, your OTel Collector.

AOTel handles the "First Mile": Ultra-fast ingestion, validation, and local node buffering with 0-GC impact.

OTel Collector handles the "Brain": Complex routing, PII masking, tail-sampling, and multi-destination exporting.

By offloading the raw ingestion to AOTel (Native AOT), you can scale down your main Collector’s CPU/RAM requirements.

## 🛠️ Getting Started: A Drop-in Ingestion Layer

AOTel implements the standard OpenTelemetry Protocol (OTLP) HTTP ingest endpoint. You do not need to change a single line of your application code or observability backend to start using it.

### 1. Run via Docker

You can evaluate AOTel locally in front of an existing Jaeger or OTel Collector instance using Docker:

```bash
docker run -d \
  --name aotel-proxy \
  -p 4318:4318 \
  -e AOTEL_UPSTREAM_URL="http://jaeger-collector:4318" \
  aotel:latest
```

### 2. Deploy to Kubernetes

AOTel is best utilized as a DaemonSet. Apply the optimized manifest to your cluster to deploy one AOTel instance per node:

```yaml
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: aotel-proxy
  namespace: observability
spec:
  selector:
    matchLabels:
      app: aotel-proxy
  template:
    metadata:
      labels:
        app: aotel-proxy
    spec:
      containers:
        - name: aotel
          image: aotel:latest
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 4318
              hostPort: 4318
              protocol: TCP
              name: otlp-http
          env:
            # Route traffic to your central OTel Collector service
            - name: AOTEL_UPSTREAM_URL
              value: "http://otel-collector.observability.svc.cluster.local:4318"
          resources:
            requests:
              cpu: 10m
              memory: 15Mi
            limits:
              cpu: 100m
              memory: 30Mi
          securityContext:
            readOnlyRootFilesystem: true
            allowPrivilegeEscalation: false
            capabilities:
              drop: ["ALL"]
```

### 3. Point your Applications to AOTel

Update your application pods to send OTLP traffic to the local node's IP rather than the central collector.

For Kubernetes pods, you can expose the host IP via the Downward API, or simply set your OTEL exporter endpoint:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT="http://$(HOST_IP):4318"
```

*Enjoy the edge-optimized performance!*
