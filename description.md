# Pessoa 1 - Infraestrutura e Stubs

## O que foi feito

A Pessoa 1 preparou a infraestrutura local para o spike de integracao
omnichannel. O objetivo foi deixar os servicos externos simulados e o broker de
mensagens prontos para que as Pessoas 2 e 3 possam ligar o outbox e o
Integration Worker sem voltar a redesenhar o ambiente Docker.

## Componentes entregues

### Docker Compose

O ficheiro `docker-compose.yml` foi atualizado para incluir:

- `nopcommerce_web`, exposto em `http://localhost:8080`;
- `nopcommerce_database`, usando SQL Server 2019 com volume persistente;
- `rabbitmq`, com AMQP em `localhost:5672` e management UI em
  `http://localhost:15672`;
- `warehouse_stub`, exposto em `http://localhost:5081`;
- `shipping_stub`, exposto em `http://localhost:5082`.

Tambem foram adicionados healthchecks para a base de dados, RabbitMQ e stubs.
O nopCommerce depende apenas da base de dados estar healthy, mantendo a ideia
arquitetural de que warehouse e shipping nao devem bloquear o arranque da loja.

### RabbitMQ

Foi criada configuracao RabbitMQ em `docker/rabbitmq/`:

- `docker/rabbitmq/rabbitmq.conf`;
- `docker/rabbitmq/definitions.json`.

As definitions criam automaticamente:

- exchange `verdemart.integration`;
- exchange dead-letter tecnico `verdemart.integration.dlx`;
- queue `fulfillment.requests`;
- queue `shipping.requests`;
- queue `fulfillment.dead`;
- queue `shipping.dead`;
- bindings para `fulfillment.requested`, `shipping.requested`,
  `fulfillment.dead` e `shipping.dead`.

A UI de gestao usa as credenciais:

- username: `guest`;
- password: `guest`.

### Warehouse Stub

Foi criado o stub em Python/FastAPI em `src/Stubs/WarehouseStub`.

Endpoints:

- `GET /health`;
- `POST /fulfillment`.

Modos suportados por variaveis de ambiente:

- `WAREHOUSE_STUB_MODE=normal`: devolve sucesso com estado `picked`;
- `WAREHOUSE_STUB_MODE=slow` ou `delayed`: simula lentidao;
- `WAREHOUSE_STUB_MODE=failed`: devolve erro `500`.

Delay configuravel:

- `WAREHOUSE_STUB_DELAY_MS`.

### Shipping Stub

Foi criado o stub em Python/FastAPI em `src/Stubs/ShippingStub`.

Endpoints:

- `GET /health`;
- `POST /labels`.

Modos suportados por variaveis de ambiente:

- `SHIPPING_STUB_MODE=normal`: devolve sucesso com estado `label_created`;
- `SHIPPING_STUB_MODE=slow`: simula lentidao;
- `SHIPPING_STUB_MODE=outage`: devolve erro `503`.

Delay configuravel:

- `SHIPPING_STUB_DELAY_MS`.

## Como validar

Arrancar o ambiente:

```bash
docker compose up --build
```

Noutro terminal, confirmar os stubs:

```bash
curl http://localhost:5081/health
curl http://localhost:5082/health
```

Respostas esperadas:

```json
{"status":"ok","service":"warehouse_stub","mode":"normal"}
{"status":"ok","service":"shipping_stub","mode":"normal"}
```

Testar pedidos normais:

```bash
curl -i -X POST http://localhost:5081/fulfillment \
  -H "Content-Type: application/json" \
  -d '{"orderId":123,"correlationId":"order-123","idempotencyKey":"test-key","items":[{"sku":"SKU-1","quantity":2}]}'

curl -i -X POST http://localhost:5082/labels \
  -H "Content-Type: application/json" \
  -d '{"orderId":123,"correlationId":"order-123","idempotencyKey":"test-key","recipient":{"name":"Customer","address":"Address"}}'
```

Abrir RabbitMQ:

```text
http://localhost:15672
```

Confirmar que existem:

- exchanges `verdemart.integration` e `verdemart.integration.dlx`;
- queues `fulfillment.requests`, `shipping.requests`, `fulfillment.dead` e
  `shipping.dead`.

## O que nao foi feito pela Pessoa 1

Ficou fora do escopo da Pessoa 1:

- migrations do nopCommerce;
- tabela outbox;
- tabela dead-letter operacional;
- tabela de idempotencia;
- alteracoes no `OrderProcessingService`;
- Integration Worker real;
- Operations View no admin.

Essas partes pertencem as Pessoas 2, 3 e 4.
