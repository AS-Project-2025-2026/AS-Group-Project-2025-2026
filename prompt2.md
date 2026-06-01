# Prompt para executar o trabalho da Pessoa 2

Usa este prompt quando quiseres que o Codex implemente a parte da Pessoa 2.

---

Estas no repositorio `/home/hugod/AS-Group-Project-2025-2026`. Implementa a
Pessoa 2 do `plano.md`: Alteracoes ao nopCommerce Core.

Objetivo: adicionar ao nopCommerce as tabelas e o codigo minimo para registar
trabalho de integracao duravel quando uma encomenda e aceite, e expor uma view
admin simples para operadores verem outbox/dead-letter e fazerem requeue.

Nao implementes o Integration Worker real. Nao publiques para RabbitMQ. Nao
chames os stubs HTTP diretamente a partir do checkout. A Pessoa 2 deve preparar
o core e a base de dados para que a Pessoa 3 processe o outbox depois.

Antes de editar, le:

- `plano.md`
- `docs/report/chapters/06-target-architecture.tex`
- `docs/report/chapters/07-architectural-decisions.tex`
- `docs/report/chapters/10-feasibility-spike.tex`
- `src/Libraries/Nop.Core/Domain/Orders/Order.cs`
- `src/Libraries/Nop.Services/Orders/OrderProcessingService.cs`
- `src/Libraries/Nop.Data/Extensions/FluentMigratorExtensions.cs`
- exemplos de migrations em `src/Libraries/Nop.Data/Migrations/UpgradeTo500`
- exemplos de builders em `src/Libraries/Nop.Data/Mapping/Builders`
- exemplos de admin list pages em:
  - `src/Presentation/Nop.Web/Areas/Admin/Controllers/ScheduleTaskController.cs`
  - `src/Presentation/Nop.Web/Areas/Admin/Views/ScheduleTask/List.cshtml`
  - `src/Presentation/Nop.Web/Areas/Admin/Models/ScheduleTasks`
  - `src/Presentation/Nop.Web/Areas/Admin/Factories/ScheduleTaskModelFactory.cs`

## Escopo funcional

1. Criar entidades core para integracao:
   - `OutboxRecord`
   - `DeadLetterRecord`
   - `IdempotencyRecord`

2. Criar schema/migration para as 3 tabelas.

3. Criar servico do core nopCommerce para trabalhar com estes registos:
   - inserir outbox;
   - listar outbox/dead-letter para admin;
   - requeue de outbox falhado;
   - requeue de dead-letter para outbox;
   - criar/consultar idempotency records, mesmo que inicialmente seja API
     simples para uso futuro da Pessoa 3.

4. Ligar o fluxo de order placement:
   - quando uma encomenda e aceite, criar exatamente um `OutboxRecord`;
   - `MessageType` inicial: `FulfillmentRequested`;
   - `Status` inicial: `Pending`;
   - `RetryCount`: `0`;
   - `CreatedAtUtc`: data atual UTC;
   - `NextAttemptAtUtc`: data atual UTC;
   - `PublishedAtUtc`: `null`;
   - `CorrelationId`: `order-{OrderId}`;
   - `IdempotencyKey`: UUID gerado uma vez e guardado no outbox;
   - `Payload`: JSON suficiente para o worker chamar o Warehouse Stub.

5. Criar uma Operations View no admin:
   - lista outbox pendente/retrying/published/failed;
   - lista dead-letters;
   - mostra uma secao simples de circuit breaker como placeholder, por exemplo
     "Not implemented by Pessoa 2 / owned by Pessoa 4";
   - botao de requeue para dead-letter;
   - botao de requeue para outbox falhado.

## Modelo de dados esperado

Cria as entidades numa pasta propria, por exemplo:

- `src/Libraries/Nop.Core/Domain/Integration/OutboxRecord.cs`
- `src/Libraries/Nop.Core/Domain/Integration/DeadLetterRecord.cs`
- `src/Libraries/Nop.Core/Domain/Integration/IdempotencyRecord.cs`

Usa `BaseEntity` como nas outras entidades nopCommerce.

### `OutboxRecord`

Campos minimos:

- `int OrderId`
- `string MessageType`
- `string Payload`
- `string CorrelationId`
- `string IdempotencyKey`
- `string Status`
- `int RetryCount`
- `DateTime CreatedAtUtc`
- `DateTime? NextAttemptAtUtc`
- `DateTime? PublishedAtUtc`
- `string LastError`

Status esperados inicialmente:

- `Pending`
- `Retrying`
- `Published`
- `Failed`

### `DeadLetterRecord`

Campos minimos:

- `int? OriginalOutboxRecordId`
- `string Payload`
- `string IdempotencyKey`
- `string CorrelationId`
- `string Adapter`
- `string FailureReason`
- `DateTime? FirstAttemptAtUtc`
- `DateTime? LastAttemptAtUtc`
- `string EscalationState`
- `DateTime CreatedAtUtc`
- `DateTime? ResolvedAtUtc`

Escalation states esperados:

- `New`
- `Acknowledged`
- `Resolved`
- `Requeued`

### `IdempotencyRecord`

Campos minimos:

- `string Key`
- `string Outcome`
- `DateTime CreatedAtUtc`
- `DateTime? ExpiresAtUtc`

## Mapping e migrations

Cria builders seguindo os padroes em `src/Libraries/Nop.Data/Mapping/Builders`,
por exemplo:

- `src/Libraries/Nop.Data/Mapping/Builders/Integration/OutboxRecordBuilder.cs`
- `src/Libraries/Nop.Data/Mapping/Builders/Integration/DeadLetterRecordBuilder.cs`
- `src/Libraries/Nop.Data/Mapping/Builders/Integration/IdempotencyRecordBuilder.cs`

Define tamanhos razoaveis para strings:

- `MessageType`: 100
- `CorrelationId`: 100
- `IdempotencyKey`: 100
- `Status`: 50
- `Adapter`: 100
- `EscalationState`: 50
- `Payload`: usar tipo equivalente a texto largo quando o builder/padrao
  permitir; se o padrao local nao facilitar, usar tamanho suficientemente alto.
- `LastError` e `FailureReason`: 1000 ou texto largo.

Cria uma migration de schema seguindo o padrao local. Usa timestamp unico e
descricao clara, por exemplo:

- `src/Libraries/Nop.Data/Migrations/UpgradeTo500/OmnichannelIntegrationRecordsMigration.cs`

A migration deve criar as tabelas se nao existirem, usando helpers locais como
`CreateTableIfNotExists<TEntity>()` quando isso encaixar no padrao.

Adicionar indices quando for simples e suportado pelo padrao local:

- `OutboxRecord.Status`
- `OutboxRecord.NextAttemptAtUtc`
- `OutboxRecord.OrderId`
- `OutboxRecord.IdempotencyKey`
- `DeadLetterRecord.EscalationState`
- `DeadLetterRecord.CorrelationId`
- `IdempotencyRecord.Key`, idealmente unico.

## Servico de integracao no nopCommerce

Cria um servico em `src/Libraries/Nop.Services/Integration`, por exemplo:

- `IIntegrationRecordService.cs`
- `IntegrationRecordService.cs`
- opcional: constantes/statuses em `IntegrationDefaults.cs`

Responsabilidades minimas:

- `Task<OutboxRecord> CreateFulfillmentOutboxRecordAsync(Order order)`
- `Task<IPagedList<OutboxRecord>> SearchOutboxRecordsAsync(...)`
- `Task<IPagedList<DeadLetterRecord>> SearchDeadLetterRecordsAsync(...)`
- `Task RequeueOutboxRecordAsync(int outboxRecordId)`
- `Task RequeueDeadLetterRecordAsync(int deadLetterRecordId)`
- `Task<IdempotencyRecord> GetIdempotencyRecordByKeyAsync(string key)`
- `Task InsertIdempotencyRecordAsync(IdempotencyRecord record)`

Usa `IRepository<TEntity>` como os restantes servicos nopCommerce.

Payload recomendado para `FulfillmentRequested`:

```json
{
  "orderId": 123,
  "correlationId": "order-123",
  "idempotencyKey": "uuid",
  "items": [
    { "productId": 1, "sku": "SKU-1", "quantity": 2 }
  ]
}
```

Se obter SKU exigir servicos adicionais ou carregar produto por item for
demasiado invasivo, inclui pelo menos `productId` e `quantity`, e deixa `sku`
como string vazia ou omitida. Nao bloqueies o outbox por dados opcionais.

## Hook no order placement

Atualiza `OrderProcessingService` para depender do novo
`IIntegrationRecordService`.

Procura o caminho de sucesso de `PlaceOrderAsync`, onde a encomenda ja foi
criada e antes/depois do `OrderPlacedEvent`. Cria o outbox no mesmo caminho de
sucesso, sem chamar RabbitMQ, HTTP ou servicos externos.

Requisitos:

- Uma encomenda aceite cria um e apenas um `OutboxRecord`.
- O checkout nao deve depender de RabbitMQ, Warehouse Stub ou Shipping Stub.
- O erro de integracao nao deve ser escondido se impedir persistencia local; se
  o outbox falhar no mesmo caminho transacional, a encomenda nao deve ficar
  falsamente marcada como integrada.
- Nao criar outbox para encomendas que falharam.

Se encontrares uma transacao explicita no fluxo de order placement, insere o
outbox dentro dela. Se nao houver transacao explicita facil de usar, insere o
outbox imediatamente apos a criacao persistida da encomenda e documenta essa
limitacao no comentario final.

## Operations View admin

Implementa uma pagina admin simples. Mantem o design utilitario e consistente
com o admin existente.

Ficheiros sugeridos:

- `src/Presentation/Nop.Web/Areas/Admin/Controllers/OperationsController.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Models/Operations/OperationsSearchModel.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Models/Operations/OutboxRecordModel.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Models/Operations/OutboxRecordListModel.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Models/Operations/DeadLetterRecordModel.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Models/Operations/DeadLetterRecordListModel.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Factories/IOperationsModelFactory.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Factories/OperationsModelFactory.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Views/Operations/List.cshtml`

Padroes a seguir:

- Controller herda de `BaseAdminController`.
- Usar permissao existente `StandardPermission.System.MANAGE_MAINTENANCE` ou
  outra permissao System equivalente, para nao criar sistema de permissoes novo.
- Usar `BaseSearchModel`, `BasePagedListModel<T>`, `BaseNopEntityModel` e
  DataTables como as paginas admin existentes.
- A view deve ter duas grids ou duas tabs:
  - Outbox;
  - Dead Letters.
- O botao de requeue deve chamar actions POST com anti-forgery token.
- Se adicionar entrada ao menu admin for demasiado invasivo, pelo menos deixar
  a pagina acessivel diretamente em `/Admin/Operations/List` e mencionar isso no
  final. Idealmente adiciona item de menu em System/Operations se encontrares o
  padrao local sem grande refactor.

Requeue esperado:

- Outbox `Failed` ou `Retrying` volta para:
  - `Status = Pending`
  - `RetryCount = 0`
  - `NextAttemptAtUtc = DateTime.UtcNow`
  - `LastError = null`
  - `PublishedAtUtc = null`
- Dead-letter `New` ou `Acknowledged` cria novo outbox `Pending` com payload,
  idempotency key e correlation id originais; marca dead-letter como `Requeued`
  e preenche `ResolvedAtUtc`.

## Validacao

Antes de terminar, corre o maximo viavel:

```bash
dotnet build src/NopCommerce.sln --no-incremental
```

Se o build completo for demasiado pesado ou falhar por dependencias externas,
tenta pelo menos:

```bash
dotnet build src/Libraries/Nop.Core/Nop.Core.csproj
dotnet build src/Libraries/Nop.Data/Nop.Data.csproj
dotnet build src/Libraries/Nop.Services/Nop.Services.csproj
dotnet build src/Presentation/Nop.Web/Nop.Web.csproj
```

Tambem corre:

```bash
docker compose config
```

Se conseguires arrancar a app, valida manualmente:

- `/Admin/Operations/List` abre;
- grids carregam;
- requeue em registo elegivel nao rebenta;
- criar uma encomenda gera um `OutboxRecord`.

## Regras importantes

- Nao alteres a infraestrutura da Pessoa 1 salvo necessidade real.
- Nao implementes o Integration Worker.
- Nao publiques mensagens para RabbitMQ nesta parte.
- Nao chames Warehouse Stub nem Shipping Stub durante checkout.
- Nao mexas em `plano.md`, `prompt.md`, `prompt2.md` ou `description.md`
  salvo se o utilizador pedir explicitamente.
- Preserva alteracoes existentes do utilizador.
- Usa `apply_patch` para edicoes manuais.
- No fim, responde em portugues com:
  - ficheiros alterados/criados;
  - como o outbox e criado no order placement;
  - como abrir a Operations View;
  - comandos de validacao executados e resultado;
  - limitacoes reais que ficam para Pessoas 3/4.
