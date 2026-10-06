# Deploy บน Kubernetes

คู่มือนี้ใช้กับ Kubernetes ของ Docker Desktop บนเครื่องนักพัฒนา สำหรับ cluster จริงดูหัวข้อ "ก่อนขึ้น production" ด้านล่าง

> **สถานะการทดสอบ:** manifest ชุดนี้ผ่านการ render ด้วย `kubectl kustomize` แล้ว แต่ **ยังไม่เคยถูก deploy จริง** เพราะตอนที่เขียน เครื่องยังไม่ได้เปิด Kubernetes ระบบเดียวกันนี้ผ่านการทดสอบครบวงจรบน docker compose แล้ว ขั้นตอนตรวจผลข้อ 6 ด้านล่างคือสิ่งที่ต้องทำเมื่อ deploy ครั้งแรก

## สิ่งที่ถูกสร้าง

```
Browser ──HTTPS──> Traefik (load balancer, TLS, rate limit, security headers)
                     ├── /      → web   2 replica  (Next.js)
                     └── /api/* → api   3–10 replica (HPA ตาม CPU 70%)
                                          ├── mssql  StatefulSet 1 replica + PVC 5Gi
                                          └── kafka  StatefulSet 1 replica + PVC 2Gi
```

| ไฟล์ | เนื้อหา |
|---|---|
| `namespace.yaml` | namespace `shop` |
| `mssql.yaml`, `kafka.yaml` | ฐานข้อมูลและ Kafka (โหมด KRaft) |
| `migrate-job.yaml` | Job ที่สร้างตาราง ใส่ข้อมูลตัวอย่าง และสร้าง login `shop_app` |
| `api.yaml` | Deployment, Service, HorizontalPodAutoscaler, PodDisruptionBudget |
| `web.yaml` | Deployment, Service |
| `ingress.yaml` | Ingress และ Traefik Middleware |
| `networkpolicy.yaml` | ปิดทุกเส้นทางแล้วเปิดเฉพาะที่จำเป็น |

## ขั้นตอน

### 1. เปิด Kubernetes

Docker Desktop → Settings → Kubernetes → Enable Kubernetes แล้วรอจนสถานะเป็น running

```bash
kubectl get nodes
```

### 2. ติดตั้ง Traefik และ metrics-server

ต้องมี [Helm](https://helm.sh/docs/intro/install/)

```bash
helm repo add traefik https://traefik.github.io/charts
```

```bash
helm upgrade --install traefik traefik/traefik --namespace traefik --create-namespace
```

```bash
helm repo add metrics-server https://kubernetes-sigs.github.io/metrics-server/
```

```bash
helm upgrade --install metrics-server metrics-server/metrics-server --namespace kube-system --set "args={--kubelet-insecure-tls}"
```

ถ้าไม่มี metrics-server ตัว HPA จะไม่มีตัวเลข CPU และจะไม่ scale (`--kubelet-insecure-tls` จำเป็นเฉพาะบน Docker Desktop)

### 3. สร้าง secret

```bash
cp deploy/k8s/secrets.example.env deploy/k8s/secrets.env
```

ใส่ค่าทั้งสามตัวใน `secrets.env` (ไฟล์นี้ถูก git ignore) แล้วสร้างใบรับรอง TLS แบบ self-signed สำหรับ `shop.localhost`

```bash
kubectl create namespace shop
```

```bash
openssl req -x509 -nodes -newkey rsa:2048 -days 365 -subj "/CN=shop.localhost" -addext "subjectAltName=DNS:shop.localhost" -keyout shop-tls.key -out shop-tls.crt
```

```bash
kubectl create secret tls shop-tls --namespace shop --cert shop-tls.crt --key shop-tls.key
```

ลบไฟล์ `shop-tls.key` และ `shop-tls.crt` หลังสร้าง secret แล้ว

### 4. Build image

Docker Desktop ใช้ image จากเครื่องได้โดยตรง ไม่ต้อง push

```bash
docker build -t shop-api:local backend
```

```bash
docker build -t shop-web:local frontend
```

### 5. Deploy

```bash
kubectl apply -k deploy/k8s
```

### 6. ตรวจผล

```bash
kubectl -n shop wait --for=condition=complete job/migrate --timeout=300s
```

```bash
kubectl -n shop get pods
```

ผลที่ต้องได้: `api` 3 ตัว, `web` 2 ตัว, `mssql` และ `kafka` อย่างละตัว ทั้งหมด Ready

| ตรวจ | คำสั่ง | ผลที่ต้องได้ |
|---|---|---|
| API ผ่าน load balancer | `curl -k https://shop.localhost/api/products` | JSON สินค้า 12 รายการ |
| HTTP ถูกส่งไป HTTPS | `curl -I http://shop.localhost` | สถานะ 301 หรือ 308 |
| Header ความปลอดภัย | `curl -kI https://shop.localhost` | มี `content-security-policy` ชุดเดียว, `strict-transport-security`, `x-content-type-options: nosniff` |
| ซื้อและยกเลิก | เปิด https://shop.localhost ใน browser | ชำระเงินได้ และยกเลิกแล้วสต๊อกกลับคืน |
| ทนต่อ pod ตาย | `kubectl -n shop delete pod -l app.kubernetes.io/name=api --wait=false` ทีละตัวระหว่างใช้งาน | หน้าเว็บยังใช้ได้ |
| HPA | `kubectl -n shop get hpa` | คอลัมน์ TARGETS เป็นตัวเลข ไม่ใช่ `<unknown>` |

Browser จะเตือนเรื่องใบรับรองเพราะเป็น self-signed ซึ่งเป็นเรื่องปกติของเครื่องนักพัฒนา

### ลบทั้งหมด

คำสั่งนี้ลบข้อมูลในฐานข้อมูลด้วย

```bash
kubectl delete namespace shop
```

## ข้อจำกัดบน Docker Desktop

- **NetworkPolicy ไม่ถูกบังคับใช้** CNI ที่มากับ Docker Desktop ไม่รองรับ manifest ถูกต้องและจะมีผลบน cluster ที่ใช้ CNI อย่าง Calico หรือ Cilium
- **มี node เดียว** การกระจาย pod ข้าม node (`topologySpreadConstraints`) จึงไม่มีผล
- **ชื่อ namespace ของ Traefik** `networkpolicy.yaml` อนุญาตการเข้าจาก namespace ชื่อ `traefik` ถ้าติดตั้ง Traefik ไว้ที่อื่นต้องแก้ไฟล์นี้

## ก่อนขึ้น production

| เรื่อง | ต้องทำ |
|---|---|
| Image | push ขึ้น registry ใช้ tag ที่ระบุเวอร์ชัน และแก้ `image:` ใน `api.yaml`, `web.yaml`, `migrate-job.yaml` |
| ฐานข้อมูล | ใช้ SQL Server แบบ managed หรือ availability group; เอา `TrustServerCertificate=True` ออกและใช้ใบรับรองจริง |
| Kafka | อย่างน้อย 3 broker เปิด TLS และ SASL; ตั้ง replication factor ของ topic เป็น 3 |
| Secret | ใช้ตัวจัดการ secret ขององค์กร (เช่น External Secrets, Sealed Secrets) แทนไฟล์ `secrets.env` |
| TLS | ใช้ใบรับรองจริง เช่น ผ่าน cert-manager |
| `ForwardedHeaders__KnownNetworks` | ตั้งเป็น CIDR ของ pod ใน cluster นั้น ไม่เช่นนั้น rate limit จะนับ IP ผิด |
| Rate limit | ตัวนับใน API อยู่ในหน่วยความจำของแต่ละ pod เพดานจริงจึงเท่ากับค่าที่ตั้งคูณจำนวน pod |
| Observability | เพิ่ม metrics, tracing และการแจ้งเตือนเมื่อ outbox ค้างหรือมีข้อความใน `shop.events.dlq` |
