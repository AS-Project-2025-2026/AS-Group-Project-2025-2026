# Prompt para executar o trabalho da Pessoa 1

Usa este prompt quando quiseres que o Codex implemente a parte da Pessoa 1.

---

Estas no repositorio `/home/hugod/AS-Group-Project-2025-2026`. Implementa a
Pessoa 1 do `plano.md`: Infraestrutura e Stubs.

Objetivo: deixar o ambiente local a arrancar com `docker compose up --build`,
incluindo SQL Server, nopCommerce, RabbitMQ com exchanges/queues criadas
automaticamente, Warehouse Stub e Shipping Stub. Nao implementes ainda as
migrations, o hook no `OrderProcessingService`, a Operations View nem a logica
real do Integration Worker.

Antes de editar, le:

- `plano.md`
- `docker-compose.yml`
- `Dockerfile`
- `README.md`
- `global.json`

Faz as alteracoes seguintes:

1. Atualiza `docker-compose.yml`.
   - Mantem `nopcommerce_database` com SQL Server 2019.
   - Adiciona volume persistente para SQL Server.
   - Adiciona healthcheck para a DB.
   - Muda `nopcommerce_web` para expor `8080:80`.
   - Faz `nopcommerce_web` depender apenas da DB healthy.
   - Adiciona `rabbitmq` com imagem `rabbitmq:3-management`.
   - Expõe `5672:5672` e `15672:15672`.
   - Monta definicoes RabbitMQ a partir de `docker/rabbitmq/definitions.json`.
   - Monta config RabbitMQ a partir de `docker/rabbitmq/rabbitmq.conf`.
   - Adiciona healthcheck de RabbitMQ.
   - Adiciona `warehouse_stub` em `5081:8080`.
   - Adiciona `shipping_stub` em `5082:8080`.
   - Usa env overrides:
     - `WAREHOUSE_STUB_MODE`, default `normal`
     - `WAREHOUSE_STUB_DELAY_MS`, default `0`
     - `SHIPPING_STUB_MODE`, default `normal`
     - `SHIPPING_STUB_DELAY_MS`, default `0`
   - Se criares `integration_worker`, deixa-o apenas como profile opcional
     `integration` ou nao o cries ainda. Nao deve bloquear o compose base.

2. Cria `docker/rabbitmq/definitions.json`.
   - Exchange principal: `verdemart.integration`, tipo `direct`, durable.
   - Exchange DLX tecnico: `verdemart.integration.dlx`, tipo `direct`, durable.
   - Queues duraveis:
     - `fulfillment.requests`
     - `shipping.requests`
     - `fulfillment.dead`
     - `shipping.dead`
   - Bindings:
     - `fulfillment.requested` -> `fulfillment.requests`
     - `shipping.requested` -> `shipping.requests`
     - `fulfillment.dead` -> `fulfillment.dead`
     - `shipping.dead` -> `shipping.dead`
   - `fulfillment.requests` deve ter DLX `verdemart.integration.dlx` e routing
     key `fulfillment.dead`.
   - `shipping.requests` deve ter DLX `verdemart.integration.dlx` e routing key
     `shipping.dead`.

3. Cria `docker/rabbitmq/rabbitmq.conf`.
   - Conteudo minimo:

```conf
management.load_definitions = /etc/rabbitmq/definitions.json
```

4. Cria Warehouse Stub em Python com FastAPI.
   - Caminho: `src/Stubs/WarehouseStub`.
   - Ficheiros esperados:
     - `app.py`
     - `requirements.txt`
     - `Dockerfile`
   - Usa uma imagem Python slim no Dockerfile.
   - O container deve ouvir em `0.0.0.0:8080`.
   - Arranca com `uvicorn app:app --host 0.0.0.0 --port 8080`.
   - Usa dependencias minimas:
     - `fastapi==0.115.6`
     - `uvicorn[standard]==0.34.0`
   - Endpoint `GET /health` devolve JSON com `status`, `service` e `mode`.
   - Endpoint `POST /fulfillment`:
     - modo `normal`: `202 Accepted`, status `picked`;
     - modo `slow` ou `delayed`: espera `STUB_DELAY_MS` ou 15000 ms se nao
       vier configurado;
     - modo `failed`: `500`, status `failed`, reason
       `warehouse_simulated_failure`.
   - O endpoint deve devolver sempre `correlationId` se vier no request.

5. Cria Shipping Stub em Python com FastAPI.
   - Caminho: `src/Stubs/ShippingStub`.
   - Ficheiros esperados:
     - `app.py`
     - `requirements.txt`
     - `Dockerfile`
   - Usa uma imagem Python slim no Dockerfile.
   - O container deve ouvir em `0.0.0.0:8080`.
   - Arranca com `uvicorn app:app --host 0.0.0.0 --port 8080`.
   - Usa dependencias minimas:
     - `fastapi==0.115.6`
     - `uvicorn[standard]==0.34.0`
   - Endpoint `GET /health` devolve JSON com `status`, `service` e `mode`.
   - Endpoint `POST /labels`:
     - modo `normal`: `201 Created`, status `label_created`, tracking number
       previsivel;
     - modo `slow`: espera `STUB_DELAY_MS` ou 15000 ms se nao vier configurado;
     - modo `outage`: `503`, status `unavailable`, reason
       `shipping_simulated_outage`.
   - O endpoint deve devolver sempre `correlationId` se vier no request.

6. Opcional mas recomendado: cria `src/Stubs/README.md`.
   - Documenta endpoints, modos, portas e comandos curl curtos.

7. Valida antes de terminar.
   - Corre `docker compose config`.
   - Corre `python -m py_compile` nos dois ficheiros `app.py`, se Python
     estiver disponivel localmente.
   - Se for viavel no ambiente, corre `docker compose build warehouse_stub
     shipping_stub rabbitmq`.
   - Nao deixes sessoes longas de `docker compose up` abertas no fim.

Regras importantes:

- Nao mexas em ficheiros fora do escopo da Pessoa 1 salvo necessidade real.
- Nao alteres `OrderProcessingService`.
- Nao cries migrations.
- Nao implementes o worker real.
- Preserva alteracoes existentes do utilizador.
- Usa `apply_patch` para edicoes manuais.
- No fim, responde em portugues com:
  - ficheiros alterados/criados;
  - comandos de validacao executados e resultado;
  - qualquer pendente real para Pessoas 2/3/4.
