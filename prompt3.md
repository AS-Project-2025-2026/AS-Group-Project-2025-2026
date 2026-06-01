# Prompt para executar o trabalho da Pessoa 3

Usa este prompt quando quiseres que o Codex implemente a parte da Pessoa 3.

---

Estas no repositorio `/home/hugod/AS-Group-Project-2025-2026`. Implementa a
Pessoa 3 do `plano.md`: Integration Worker.

Objetivo: criar um servico .NET separado que leia `OutboxRecord` pendentes,
publique comandos para RabbitMQ, consuma os comandos das queues, chame os stubs
HTTP de warehouse/shipping e atualize o estado do outbox/idempotency no
nopCommerce.

Nao implementes ainda Polly, circuit breaker completo, dead-letter final,
evidence pack ou demo script. Essas partes sao da Pessoa 4. A Pessoa 3 deve
entregar o fluxo normal a funcionar.

## Pre-condicoes

Este trabalho depende de:

- Pessoa 1: `docker-compose.yml`, RabbitMQ, Warehouse Stub e Shipping Stub;
- Pessoa 2: entidades/tabelas `OutboxRecord`, `DeadLetterRecord`,
  `IdempotencyRecord`, servico de integracao e hook no order placement.

Antes de editar, confirma que estas alteracoes existem no branch atual. Se as
entidades/servicos da Pessoa 2 ainda nao existirem, para e explica que a Pessoa
3 esta bloqueada por falta de schema/contratos.

## Ficheiros a ler antes de editar

- `plano.md`
- `prompt2.md`
- `docker-compose.yml`
- `docker/rabbitmq/definitions.json`
- `src/Libraries/Nop.Core/Domain/Integration/*`
- `src/Libraries/Nop.Services/Integration/*`
- `src/Libraries/Nop.Data/Configuration/DataConfig.cs`
- `src/Libraries/Nop.Data/NopDbStartup.cs`
- `src/Libraries/Nop.Data/IRepository.cs`
- exemplos de hosted services/background services no repo, se existirem
- `src/Stubs/README.md`

## Projeto a criar

Cria um projeto worker separado:

- `src/Integration/Nop.IntegrationWorker/Nop.IntegrationWorker.csproj`
- `src/Integration/Nop.IntegrationWorker/Program.cs`
- `src/Integration/Nop.IntegrationWorker/appsettings.json`
- `src/Integration/Nop.IntegrationWorker/Dockerfile`

Target framework:

- usa `net10.0`, alinhado com `global.json` e com o Dockerfile principal.

Adicionar o projeto a solucao:

```bash
dotnet sln src/NopCommerce.sln add src/Integration/Nop.IntegrationWorker/Nop.IntegrationWorker.csproj
```

Referencias esperadas:

- `src/Libraries/Nop.Core/Nop.Core.csproj`
- `src/Libraries/Nop.Data/Nop.Data.csproj`
- `src/Libraries/Nop.Services/Nop.Services.csproj`

Packages provaveis:

- `RabbitMQ.Client`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Http`
- `Microsoft.Extensions.Configuration`
- `Microsoft.Extensions.Logging.Console`

Nao adicionar Polly ainda. Pessoa 4 trata disso.

## Configuracao

O worker deve ser configuravel por env vars usadas no compose:

- `RabbitMq__Host=rabbitmq`
- `RabbitMq__Port=5672`
- `RabbitMq__Username=guest`
- `RabbitMq__Password=guest`
- `RabbitMq__Exchange=verdemart.integration`
- `RabbitMq__FulfillmentQueue=fulfillment.requests`
- `RabbitMq__ShippingQueue=shipping.requests`
- `Warehouse__BaseUrl=http://warehouse_stub:8080`
- `Shipping__BaseUrl=http://shipping_stub:8080`
- `ConnectionStrings__ConnectionString=<connection string para SQL Server>`
- `ConnectionStrings__DataProvider=SqlServer`
- `Worker__PollingIntervalSeconds=5`
- `Worker__BatchSize=20`

Atualiza `docker-compose.yml` para adicionar `integration_worker` apenas se o
projeto compilar. O servico deve depender de:

- `nopcommerce_database` healthy;
- `rabbitmq` healthy;
- `warehouse_stub` healthy;
- `shipping_stub` healthy.

Nao mudes nomes de exchanges/queues definidos pela Pessoa 1.

## Arquitetura minima do worker

Implementa componentes pequenos, por exemplo:

- `Options/RabbitMqOptions.cs`
- `Options/WorkerOptions.cs`
- `Options/WarehouseOptions.cs`
- `Options/ShippingOptions.cs`
- `Messaging/RabbitMqConnectionFactory.cs`
- `Messaging/RabbitMqPublisher.cs`
- `Messaging/RabbitMqConsumer.cs`
- `Clients/WarehouseClient.cs`
- `Clients/ShippingClient.cs`
- `Services/OutboxPollingService.cs`
- `Services/FulfillmentConsumerService.cs`
- `Services/ShippingConsumerService.cs`
- `Models/FulfillmentRequestedMessage.cs`
- `Models/ShippingRequestedMessage.cs`

Podes simplificar nomes/classes se o codigo ficar claro.

## Fluxo esperado

### 1. Polling de outbox

`OutboxPollingService` deve:

- correr como `BackgroundService`;
- procurar `OutboxRecord` com `Status = Pending` e
  `NextAttemptAtUtc <= DateTime.UtcNow`;
- respeitar `Worker__BatchSize`;
- publicar cada payload em RabbitMQ:
  - `MessageType = FulfillmentRequested` -> routing key
    `fulfillment.requested`;
  - `MessageType = ShippingRequested` -> routing key `shipping.requested`,
    se a Pessoa 2 ou o proprio worker criar esse tipo;
- marcar o outbox como `Published` apenas depois do publish com sucesso;
- em erro simples, marcar `Status = Retrying`, incrementar `RetryCount`,
  preencher `LastError` e `NextAttemptAtUtc` com alguns segundos no futuro.

Nota: resiliencia sofisticada, backoff exponencial e dead-letter final ficam
para Pessoa 4. Aqui basta uma politica simples para o fluxo normal nao perder
estado.

### 2. Consumer de fulfillment

`FulfillmentConsumerService` deve:

- consumir queue `fulfillment.requests`;
- parsear payload do outbox;
- verificar `IdempotencyRecord` antes de chamar o warehouse:
  - se a key ja tiver outcome de sucesso, dar ack e nao repetir side effect;
  - se nao existir, continuar;
- chamar `POST {Warehouse__BaseUrl}/fulfillment`;
- em resposta 2xx:
  - gravar/atualizar `IdempotencyRecord` com outcome de sucesso;
  - criar um novo `OutboxRecord` do tipo `ShippingRequested`, ou publicar
    diretamente `shipping.requested` se o contrato da Pessoa 2 nao tiver metodo
    para criar outbox de shipping;
  - dar ack no RabbitMQ;
- em resposta nao 2xx ou timeout:
  - dar nack/requeue simples ou atualizar outbox como retrying se houver
    referencia clara;
  - nao criar dead-letter final ainda.

### 3. Consumer de shipping

`ShippingConsumerService` deve:

- consumir queue `shipping.requests`;
- verificar idempotency;
- chamar `POST {Shipping__BaseUrl}/labels`;
- em resposta 2xx:
  - gravar outcome de sucesso em `IdempotencyRecord`;
  - dar ack;
- em erro:
  - nack/requeue simples ou marcar retrying;
  - nao mover para `DeadLetterRecord` ainda, salvo se a Pessoa 2 ja tiver API
    e for trivial criar um placeholder. A decisao final fica para Pessoa 4.

## RabbitMQ

Usa as definitions da Pessoa 1:

- exchange `verdemart.integration`, tipo direct;
- routing key `fulfillment.requested`;
- routing key `shipping.requested`;
- queue `fulfillment.requests`;
- queue `shipping.requests`.

Mensagens devem ser persistentes quando possivel.

Usa manual ack nos consumers. Nao uses auto-ack.

## Idempotencia

Antes de qualquer chamada HTTP que tenha side effect:

- consulta `IdempotencyRecord` por key;
- se existir sucesso, ack e termina;
- se nao existir, chama o stub;
- depois de sucesso, grava outcome.

Outcome pode ser JSON simples, por exemplo:

```json
{
  "status": "processed",
  "adapter": "warehouse",
  "processedAtUtc": "..."
}
```

## Docker Compose

Atualiza `docker-compose.yml` para incluir:

```yaml
integration_worker:
  build:
    context: .
    dockerfile: src/Integration/Nop.IntegrationWorker/Dockerfile
```

Passa as env vars descritas acima.

Se o compose completo ficar pesado por causa do build do nopCommerce, o worker
deve pelo menos conseguir ser buildado isoladamente com:

```bash
docker compose build integration_worker
```

## Validacao

Antes de terminar, corre:

```bash
dotnet build src/Integration/Nop.IntegrationWorker/Nop.IntegrationWorker.csproj
dotnet build src/NopCommerce.sln --no-incremental
docker compose config
docker compose build integration_worker
```

Se o build completo da solucao for demasiado pesado, explica o motivo e corre
pelo menos:

```bash
dotnet build src/Libraries/Nop.Core/Nop.Core.csproj
dotnet build src/Libraries/Nop.Data/Nop.Data.csproj
dotnet build src/Libraries/Nop.Services/Nop.Services.csproj
dotnet build src/Integration/Nop.IntegrationWorker/Nop.IntegrationWorker.csproj
```

Teste manual recomendado, se a Pessoa 2 ja permitir criar outbox:

1. Arrancar:

```bash
docker compose up --build
```

2. Criar uma encomenda no nopCommerce ou inserir um `OutboxRecord` pendente
   manualmente apenas em ambiente local de teste.

3. Confirmar logs do worker:

- outbox pendente encontrado;
- mensagem publicada em `fulfillment.requested`;
- warehouse stub chamado;
- shipping request criado/publicado;
- shipping stub chamado;
- idempotency record criado;
- outbox fica `Published`/processado.

## Regras importantes

- Nao implementar Polly/circuit breaker/dead-letter final. Pessoa 4 faz isso.
- Nao alterar os contratos RabbitMQ da Pessoa 1.
- Nao alterar a criacao do outbox no checkout feita pela Pessoa 2 salvo bug
  inevitavel.
- Nao mexer nos `.md` de prompts/planos salvo pedido explicito do utilizador.
- Preserva alteracoes existentes do utilizador.
- Usa `apply_patch` para edicoes manuais.
- No fim, responde em portugues com:
  - ficheiros alterados/criados;
  - como o worker le e publica outbox;
  - como os consumers chamam warehouse/shipping;
  - comandos de validacao executados e resultado;
  - limitacoes que ficam para Pessoa 4.
