# Plano de Implementacao - Omnichannel Integration Spike

Objetivo do trabalho: transformar a arquitetura documentada no relatorio num
slice executavel que demonstre que uma encomenda do nopCommerce pode gerar
trabalho de integracao duravel, passar por RabbitMQ e ser processada por
dependencias externas simuladas sem bloquear o checkout.

O plano original tinha a divisao certa, mas ainda nao estava completo como guia
de execucao. Faltavam contratos concretos, nomes de servicos, criterios de
aceitacao, comandos de validacao e fronteiras claras entre as pessoas. Esta
versao deixa a Pessoa 1 pronta para implementar a infraestrutura e deixa
interfaces suficientemente estaveis para as Pessoas 2, 3 e 4.

## Contexto tecnico do repositorio

- Aplicacao base: nopCommerce em .NET, solucao em `src/NopCommerce.sln`.
- Web app: `src/Presentation/Nop.Web/Nop.Web.csproj`.
- Docker atual: `Dockerfile` e `docker-compose.yml` na raiz.
- Base de dados atual no compose: SQL Server 2019.
- Documentacao arquitetural relevante:
  - `docs/report/chapters/06-target-architecture.tex`
  - `docs/report/chapters/07-architectural-decisions.tex`
  - `docs/report/chapters/10-feasibility-spike.tex`

## Contratos comuns entre pessoas

Estes nomes devem ser tratados como contrato para evitar cada pessoa inventar
um nome diferente.

### Servicos Docker

- `nopcommerce_web`: storefront/admin nopCommerce.
- `nopcommerce_database`: SQL Server.
- `rabbitmq`: message broker com management UI.
- `integration_worker`: worker .NET separado, criado pela Pessoa 3.
- `warehouse_stub`: simulador HTTP do sistema de warehouse.
- `shipping_stub`: simulador HTTP do provider de shipping.

### Portas locais sugeridas

- nopCommerce: `http://localhost:8080` mapeado para porta interna `80`.
- RabbitMQ AMQP: `localhost:5672`.
- RabbitMQ management UI: `http://localhost:15672`.
- Warehouse Stub: `http://localhost:5081`.
- Shipping Stub: `http://localhost:5082`.

Nota: o compose atual usa `80:80`. A Pessoa 1 deve mudar para `8080:80` para
evitar conflito com servicos locais e porque e mais previsivel em ambiente de
desenvolvimento.

### RabbitMQ

Usar um exchange principal para comandos de integracao e um exchange para
dead-letter tecnico do broker. A dead-letter operacional continua a ser tabela
na base de dados, como descrito no ADR 7.

- Exchange principal: `verdemart.integration`, tipo `direct`, durable.
- Exchange DLX tecnico: `verdemart.integration.dlx`, tipo `direct`, durable.
- Queue `fulfillment.requests`
  - binding key: `fulfillment.requested`
  - DLX: `verdemart.integration.dlx`
  - dead-letter routing key: `fulfillment.dead`
- Queue `shipping.requests`
  - binding key: `shipping.requested`
  - DLX: `verdemart.integration.dlx`
  - dead-letter routing key: `shipping.dead`
- Queue `fulfillment.dead`
  - bound to `verdemart.integration.dlx` with key `fulfillment.dead`
- Queue `shipping.dead`
  - bound to `verdemart.integration.dlx` with key `shipping.dead`

Para o compose, a forma mais simples e robusta e usar `rabbitmq:3-management`
com:

- `docker/rabbitmq/definitions.json` montado em
  `/etc/rabbitmq/definitions.json`;
- `docker/rabbitmq/rabbitmq.conf` montado em
  `/etc/rabbitmq/rabbitmq.conf`;
- conteudo minimo do `rabbitmq.conf`:

```conf
management.load_definitions = /etc/rabbitmq/definitions.json
```

### Variaveis de ambiente esperadas

O `integration_worker` deve receber:

- `RabbitMq__Host=rabbitmq`
- `RabbitMq__Port=5672`
- `RabbitMq__Username=guest`
- `RabbitMq__Password=guest`
- `RabbitMq__Exchange=verdemart.integration`
- `Warehouse__BaseUrl=http://warehouse_stub:8080`
- `Shipping__BaseUrl=http://shipping_stub:8080`
- `ConnectionStrings__ConnectionString=<connection string para SQL Server>`
- `ConnectionStrings__DataProvider=SqlServer`

Os stubs devem receber:

- `STUB_MODE=normal`, `slow`, `failed` ou `outage`, conforme aplicavel.
- `STUB_DELAY_MS=0` por defeito.

## Pessoa 1 - Infraestrutura e Stubs

Responsabilidade: tudo o que permite correr o ambiente em volta do nopCommerce
com um unico `docker compose up`, incluindo RabbitMQ, healthchecks, stubs HTTP e
contratos de configuracao para o worker.

### Resultado esperado

No fim da Pessoa 1, sem depender ainda das tabelas da Pessoa 2 nem do worker da
Pessoa 3, deve ser possivel executar:

```bash
docker compose up --build
```

E verificar:

- SQL Server esta healthy.
- nopCommerce arranca e fica acessivel em `http://localhost:8080`.
- RabbitMQ arranca e a UI fica acessivel em `http://localhost:15672`.
- Exchanges e queues existem no RabbitMQ.
- Warehouse Stub responde a healthcheck e a pedido normal.
- Shipping Stub responde a healthcheck e a pedido normal.
- Modos de falha dos stubs podem ser ativados por variavel de ambiente sem
  recompilar codigo.

### Ficheiros a criar ou alterar

Preferencia recomendada: implementar os stubs em Python com FastAPI. Fica mais
facil de ler, alterar e demonstrar, e o FastAPI expoe documentacao interativa
em `/docs` sem trabalho extra. Manter os stubs como servicos HTTP separados em
Docker, sem os adicionar a `src/NopCommerce.sln`.

Ficheiros esperados:

- Alterar `docker-compose.yml`.
- Criar `docker/rabbitmq/definitions.json`.
- Criar `docker/rabbitmq/rabbitmq.conf`.
- Criar `src/Stubs/WarehouseStub/app.py`.
- Criar `src/Stubs/WarehouseStub/requirements.txt`.
- Criar `src/Stubs/WarehouseStub/Dockerfile`.
- Criar `src/Stubs/ShippingStub/app.py`.
- Criar `src/Stubs/ShippingStub/requirements.txt`.
- Criar `src/Stubs/ShippingStub/Dockerfile`.
- Opcional: criar `src/Stubs/README.md` com os endpoints e modos.

Nao alterar ainda:

- `OrderProcessingService`.
- migrations do nopCommerce.
- tabelas outbox/dead-letter/idempotency.
- codigo do Integration Worker, excepto se for apenas um placeholder de compose
  comentado ou protegido por profile.

### `docker-compose.yml`

O compose deve conter:

- `nopcommerce_database`
  - imagem `mcr.microsoft.com/mssql/server:2019-latest`
  - `SA_PASSWORD=nopCommerce_db_password`
  - `ACCEPT_EULA=Y`
  - `MSSQL_PID=Express`
  - volume persistente para `/var/opt/mssql`
  - healthcheck com `sqlcmd` ou alternativa disponivel na imagem

- `nopcommerce_web`
  - build a partir do `Dockerfile` da raiz
  - porta `8080:80`
  - `depends_on` com `nopcommerce_database` healthy
  - variaveis de ambiente de ligacao a DB se forem compativeis com a app
  - nao deve depender do warehouse/shipping para arrancar

- `rabbitmq`
  - imagem `rabbitmq:3-management`
  - portas `5672:5672` e `15672:15672`
  - montar `docker/rabbitmq/definitions.json`
  - montar `docker/rabbitmq/rabbitmq.conf`
  - healthcheck com `rabbitmq-diagnostics -q ping`

- `warehouse_stub`
  - build do projeto `src/Stubs/WarehouseStub`
  - porta `5081:8080`
  - `STUB_MODE=normal`
  - `STUB_DELAY_MS=0`
  - healthcheck em `/health`

- `shipping_stub`
  - build do projeto `src/Stubs/ShippingStub`
  - porta `5082:8080`
  - `STUB_MODE=normal`
  - `STUB_DELAY_MS=0`
  - healthcheck em `/health`

- `integration_worker`
  - se o projeto ainda nao existir, deixar comentado no plano ou em profile
    `integration`.
  - quando existir, deve depender de `rabbitmq`, `warehouse_stub`,
    `shipping_stub` e `nopcommerce_database`.

Usar uma rede default do compose chega. Dar nomes de servico estaveis e nao
forcar `container_name`, a menos que o grupo precise disso para demonstracao.

### Warehouse Stub

Implementar em Python/FastAPI com:

- `src/Stubs/WarehouseStub/app.py`
- `src/Stubs/WarehouseStub/requirements.txt`
- `src/Stubs/WarehouseStub/Dockerfile`

Dependencias minimas em `requirements.txt`:

```text
fastapi==0.115.6
uvicorn[standard]==0.34.0
```

O Dockerfile deve usar uma imagem Python slim, instalar requirements e arrancar
com `uvicorn app:app --host 0.0.0.0 --port 8080`.

Endpoint minimo:

- `GET /health`
  - `200 OK`
  - resposta: `{ "status": "ok", "service": "warehouse_stub", "mode": "normal" }`

- `POST /fulfillment`
  - recebe JSON:

```json
{
  "orderId": 123,
  "correlationId": "order-123",
  "idempotencyKey": "uuid",
  "items": [
    { "sku": "SKU-1", "quantity": 2 }
  ]
}
```

  - modo `normal`: `202 Accepted`

```json
{
  "status": "picked",
  "warehouseReference": "WH-123",
  "correlationId": "order-123"
}
```

  - modo `slow`: esperar `STUB_DELAY_MS` ou 15000 ms por defeito e depois
    devolver a mesma resposta do modo normal.
  - modo `failed`: `500 Internal Server Error`

```json
{
  "status": "failed",
  "reason": "warehouse_simulated_failure",
  "correlationId": "order-123"
}
```

Tambem e util aceitar `STUB_MODE=delayed` como alias de `slow`, porque a
documentacao fala em `delayed`.

### Shipping Stub

Implementar em Python/FastAPI com:

- `src/Stubs/ShippingStub/app.py`
- `src/Stubs/ShippingStub/requirements.txt`
- `src/Stubs/ShippingStub/Dockerfile`

Dependencias minimas em `requirements.txt`:

```text
fastapi==0.115.6
uvicorn[standard]==0.34.0
```

O Dockerfile deve usar uma imagem Python slim, instalar requirements e arrancar
com `uvicorn app:app --host 0.0.0.0 --port 8080`.

Endpoint minimo:

- `GET /health`
  - `200 OK`
  - resposta: `{ "status": "ok", "service": "shipping_stub", "mode": "normal" }`

- `POST /labels`
  - recebe JSON:

```json
{
  "orderId": 123,
  "correlationId": "order-123",
  "idempotencyKey": "uuid",
  "recipient": {
    "name": "Customer",
    "address": "Address"
  }
}
```

  - modo `normal`: `201 Created`

```json
{
  "status": "label_created",
  "trackingNumber": "TRK-123",
  "carrier": "StubCarrier",
  "correlationId": "order-123"
}
```

  - modo `outage`: `503 Service Unavailable`

```json
{
  "status": "unavailable",
  "reason": "shipping_simulated_outage",
  "correlationId": "order-123"
}
```

  - modo `slow`: esperar `STUB_DELAY_MS` ou 15000 ms por defeito e depois
    devolver a resposta normal.

### Healthchecks e depends_on

Criterio pratico:

- `nopcommerce_web` depende apenas da DB healthy.
- `integration_worker` depende da DB, RabbitMQ e stubs healthy.
- Os stubs nao dependem de RabbitMQ nem da DB.
- RabbitMQ nao depende de nada.

O objetivo e que uma falha no warehouse/shipping nao impeça o nopCommerce de
arrancar, porque isso e coerente com a arquitetura assincrona.

### Validacao da Pessoa 1

Comandos de validacao:

```bash
docker compose config
docker compose up --build
```

Noutra shell:

```bash
curl http://localhost:5081/health
curl http://localhost:5082/health
curl -i -X POST http://localhost:5081/fulfillment \
  -H "Content-Type: application/json" \
  -d '{"orderId":123,"correlationId":"order-123","idempotencyKey":"test-key","items":[{"sku":"SKU-1","quantity":2}]}'
curl -i -X POST http://localhost:5082/labels \
  -H "Content-Type: application/json" \
  -d '{"orderId":123,"correlationId":"order-123","idempotencyKey":"test-key","recipient":{"name":"Customer","address":"Address"}}'
```

Verificar RabbitMQ:

- Abrir `http://localhost:15672`.
- Login default: `guest` / `guest`.
- Confirmar exchanges:
  - `verdemart.integration`
  - `verdemart.integration.dlx`
- Confirmar queues:
  - `fulfillment.requests`
  - `shipping.requests`
  - `fulfillment.dead`
  - `shipping.dead`

Teste dos modos de falha:

```bash
STUB_MODE=failed docker compose up --build warehouse_stub
STUB_MODE=outage docker compose up --build shipping_stub
```

Se for mais simples, documentar a mudanca temporaria no `docker-compose.yml`;
mas o ideal e suportar override por variavel de ambiente:

```yaml
STUB_MODE: ${WAREHOUSE_STUB_MODE:-normal}
```

### Criterios de pronto da Pessoa 1

- `docker compose config` nao falha.
- `docker compose up --build` arranca DB, web, RabbitMQ e stubs.
- Os stubs respondem aos endpoints documentados.
- RabbitMQ cria automaticamente exchanges, queues e bindings.
- O compose tem nomes e env vars que a Pessoa 3 pode usar sem alterar a
  infraestrutura.
- O nopCommerce nao depende dos stubs para iniciar.
- O que ficou pendente para Pessoa 2/3 esta explicitamente assinalado.

## Pessoa 2 - Alteracoes ao nopCommerce Core

Responsabilidade: tudo o que toca no codigo do nopCommerce e na base de dados
da aplicacao.

### Entregas

- Migration para tabela `OutboxRecord`.
- Migration para tabela `DeadLetterRecord`.
- Migration para tabela `IdempotencyRecord`.
- Hook no fluxo de order placement para criar `OutboxRecord` quando uma
  encomenda e aceite.
- Pagina admin simples "Operations View":
  - outbox pendente/retrying/published/failed;
  - dead-letters;
  - estado do circuit breaker, mesmo que inicialmente seja apenas leitura de
    uma tabela/configuracao criada pela Pessoa 4.
- Botao de requeue para dead-letter ou outbox falhado.

### Campos minimos sugeridos

`OutboxRecord`:

- `Id`
- `OrderId`
- `MessageType`
- `Payload`
- `CorrelationId`
- `IdempotencyKey`
- `Status`
- `RetryCount`
- `CreatedAtUtc`
- `NextAttemptAtUtc`
- `PublishedAtUtc`
- `LastError`

`DeadLetterRecord`:

- `Id`
- `OriginalOutboxRecordId`
- `Payload`
- `IdempotencyKey`
- `CorrelationId`
- `Adapter`
- `FailureReason`
- `FirstAttemptAtUtc`
- `LastAttemptAtUtc`
- `EscalationState`
- `CreatedAtUtc`
- `ResolvedAtUtc`

`IdempotencyRecord`:

- `Id`
- `Key`
- `Outcome`
- `CreatedAtUtc`
- `ExpiresAtUtc`

### Criterios de pronto

- Uma ordem aceite cria exatamente um `OutboxRecord`.
- O registo e criado no mesmo caminho transacional da ordem.
- A app continua a funcionar quando RabbitMQ, warehouse ou shipping estao em
  baixo.
- A Operations View permite ver o estado sem queries manuais.

## Pessoa 3 - Integration Worker

Responsabilidade: servico .NET separado que processa o outbox e interage com
RabbitMQ/stubs.

### Entregas

- Projeto `src/Integration/Nop.IntegrationWorker`.
- `IHostedService` ou `BackgroundService`.
- Polling de `OutboxRecord` pendentes.
- Publicacao para RabbitMQ:
  - `fulfillment.requested` -> `fulfillment.requests`
  - `shipping.requested` -> `shipping.requests`
- Cliente HTTP para `warehouse_stub`.
- Cliente HTTP para `shipping_stub`.
- Atualizacao de estado do outbox apos sucesso/falha.
- Consulta/escrita de `IdempotencyRecord`.
- Configuracao por env vars definidas pela Pessoa 1.

### Criterios de pronto

- Uma order/outbox pendente e processada em modo normal.
- O worker usa os nomes de RabbitMQ definidos pela Pessoa 1.
- O worker nao precisa de alterar o compose da Pessoa 1 alem de ativar o
  servico `integration_worker`.

## Pessoa 4 - Resiliencia, Evidencias e Demo

Responsabilidade: politicas de resiliencia, dead-letter operacional, logs,
evidence pack e apresentacao.

### Entregas

- Polly no Integration Worker:
  - backoff inicial 2s;
  - multiplicador x2;
  - max delay 5 min;
  - max 10 tentativas.
- Circuit breaker:
  - abre apos 5 falhas consecutivas por adapter;
  - cooldown 5 min;
  - probe automatico.
- Mover mensagens para `DeadLetterRecord` quando esgotam retries.
- Logs estruturados:
  - retry;
  - circuit open;
  - circuit half-open/probe;
  - circuit closed/recovery;
  - dead-letter criado;
  - requeue.
- Atualizacao do relatorio:
  - ADRs do capitulo 7, se necessario;
  - feasibility spike no capitulo 10;
  - secao de implementacao e evidencias.
- Demo script de 15 min com comandos exatos.

### Evidencias minimas

- Screenshot ou log do compose a arrancar.
- RabbitMQ com queues criadas.
- Modo normal: pedido processado.
- Modo warehouse lento/falhado: checkout nao bloqueia e retries aparecem.
- Modo shipping outage: label request fica pendente/retrying.
- Dead-letter apos limite.
- Recovery e requeue.

## Ordem de trabalho recomendada

1. Pessoa 1 cria infraestrutura e stubs.
2. Pessoa 2 cria tabelas e outbox no nopCommerce.
3. Pessoa 3 cria worker e fluxo normal.
4. Pessoa 4 adiciona resiliencia, dead-letter, evidencias e demo.

Pessoa 2 pode comecar em paralelo com Pessoa 1, desde que respeite os contratos
de nomes e payloads acima. Pessoa 3 deve esperar pelo minimo da Pessoa 1
RabbitMQ/stubs e pelo schema da Pessoa 2.
