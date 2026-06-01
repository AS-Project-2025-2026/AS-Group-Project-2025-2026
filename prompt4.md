# Prompt para executar o trabalho da Pessoa 4

Usa este prompt quando quiseres que o Codex implemente a parte da Pessoa 4.

---

Estas no repositorio `/home/hugod/AS-Group-Project-2025-2026`. Implementa a
Pessoa 4 do `plano.md`: Resiliencia, Dead-Letter, Evidencias e Demo.

Objetivo: reforcar o Integration Worker com retry/backoff, circuit breaker,
dead-letter operacional, logs estruturados e documentação/evidencias para demo.

Esta fase depende de:

- Pessoa 1: Docker Compose, RabbitMQ e stubs;
- Pessoa 2: tabelas `OutboxRecord`, `DeadLetterRecord`, `IdempotencyRecord` e
  Operations View;
- Pessoa 3: `src/Integration/Nop.IntegrationWorker` com fluxo normal.

Se o worker da Pessoa 3 ou as tabelas da Pessoa 2 ainda nao existirem, para e
explica o bloqueio. Nao tentes reimplementar Pessoas 2/3 do zero dentro da
Pessoa 4.

## Ficheiros a ler antes de editar

- `plano.md`
- `prompt2.md`
- `prompt3.md`
- `docs/report/chapters/07-architectural-decisions.tex`
- `docs/report/chapters/08-risk-and-validation-plan.tex`
- `docs/report/chapters/10-feasibility-spike.tex`
- `src/Integration/Nop.IntegrationWorker/**`
- `src/Libraries/Nop.Core/Domain/Integration/**`
- `src/Libraries/Nop.Services/Integration/**`
- `src/Presentation/Nop.Web/Areas/Admin/Controllers/OperationsController.cs`
- `src/Presentation/Nop.Web/Areas/Admin/Views/Operations/List.cshtml`
- `src/Stubs/README.md`

## Escopo funcional

1. Adicionar Polly ao Integration Worker.
2. Implementar retry com exponential backoff.
3. Implementar circuit breaker por adapter.
4. Mover mensagens para `DeadLetterRecord` quando esgotam retries.
5. Melhorar logs estruturados.
6. Expor estado de circuit breaker para a Operations View, se a Pessoa 2 deixou
   placeholder.
7. Criar evidence pack e demo script.
8. Atualizar o relatorio, se for viavel sem quebrar LaTeX.

## Politica de retry

Implementar no Integration Worker, nao no checkout.

Parametros obrigatorios:

- delay inicial: 2 segundos;
- multiplicador: x2;
- delay maximo: 5 minutos;
- maximo: 10 tentativas;
- retry por adapter (`warehouse`, `shipping`).

Usa Polly ou Polly.Extensions.Http, conforme encaixar melhor no codigo da
Pessoa 3.

O retry deve aplicar-se a:

- chamadas HTTP ao Warehouse Stub;
- chamadas HTTP ao Shipping Stub;
- falhas transientes de publish/consume RabbitMQ quando fizer sentido.

Nao repetir side effects se o `IdempotencyRecord` ja tiver sucesso para a key.

## Circuit breaker

Implementar circuit breaker por adapter:

- abre apos 5 falhas consecutivas;
- cooldown: 5 minutos;
- apos cooldown, permitir uma tentativa probe;
- se probe tiver sucesso, fechar o circuito;
- se probe falhar, abrir novamente e reiniciar cooldown.

O estado deve ser observavel:

- em memoria no worker para controlo runtime;
- persistido ou exposto de forma que a Operations View consiga mostrar pelo
  menos:
  - adapter;
  - state (`Closed`, `Open`, `HalfOpen`);
  - failure count;
  - opened at;
  - next probe at;
  - last error.

Se a Pessoa 2 nao criou tabela para circuit breaker, cria uma entidade/tabela
simples `CircuitBreakerStateRecord`, ou usa uma tabela/configuracao equivalente
que seja facil de listar no admin. Mantem o schema pequeno.

## Dead-letter operacional

Quando uma mensagem esgota as 10 tentativas:

- criar `DeadLetterRecord`;
- copiar payload original;
- copiar `IdempotencyKey`;
- copiar `CorrelationId`;
- preencher `Adapter`;
- preencher `FailureReason` com ultimo erro relevante;
- preencher `FirstAttemptAtUtc`;
- preencher `LastAttemptAtUtc`;
- definir `EscalationState = New`;
- marcar o outbox original como `Failed` ou estado equivalente;
- nao apagar o outbox original.

O RabbitMQ DLX tecnico da Pessoa 1 pode continuar configurado, mas a entrega
final para operadores deve ser via `DeadLetterRecord`, como ADR 7 descreve.

## Requeue

Garantir que o requeue da Operations View funciona com os novos estados:

- dead-letter `New`/`Acknowledged` pode voltar para outbox `Pending`;
- dead-letter requeued passa para `EscalationState = Requeued`;
- `ResolvedAtUtc` deve ser preenchido;
- outbox falhado pode voltar para `Pending`;
- retries devem recomecar limpos ou com contador documentado.

Se o requeue ja estiver implementado pela Pessoa 2, ajusta apenas o necessario
para respeitar os novos estados e logs.

## Logs estruturados

Adicionar logs com campos consistentes. Cada evento importante deve incluir:

- `correlationId`;
- `idempotencyKey`;
- `orderId`, quando disponivel;
- `adapter`;
- `outboxRecordId`, quando disponivel;
- `retryAttempt`;
- `circuitState`, quando aplicavel.

Eventos minimos:

- outbox picked up;
- message published;
- adapter call started;
- adapter call succeeded;
- adapter call failed;
- retry scheduled;
- circuit opened;
- circuit half-open/probe;
- circuit closed/recovered;
- dead-letter created;
- requeue requested;
- requeue completed.

Usa `ILogger<T>` e placeholders estruturados, nao concatenacao de strings.

## Operations View

Atualiza a Operations View para mostrar:

- outbox pending/retrying/published/failed;
- dead-letters;
- estado do circuit breaker;
- contadores simples:
  - pending outbox;
  - retrying outbox;
  - failed/dead-lettered;
  - circuits open.

Mantem UI simples e consistente com o admin nopCommerce. Nao criar dashboard
complexo.

## Evidence pack

Cria uma pasta para evidencias, por exemplo:

- `docs/evidence/`

Adicionar pelo menos:

- `docs/evidence/README.md`
- `docs/evidence/demo-script.md`
- opcional: `.gitkeep` em subpastas para screenshots/logs.

O README deve explicar que screenshots/logs reais podem ser adicionados apos a
execucao local.

O demo script deve ter comandos concretos para:

1. build/start:

```bash
docker compose up --build
```

2. confirmar RabbitMQ/stubs:

```bash
curl http://localhost:5081/health
curl http://localhost:5082/health
```

3. modo normal;
4. simular warehouse falhado:

```bash
WAREHOUSE_STUB_MODE=failed docker compose up --build warehouse_stub
```

ou alternativa correta com compose override/env vars, conforme o repo estiver.

5. observar retries/logs;
6. simular recovery:

```bash
WAREHOUSE_STUB_MODE=normal docker compose up --build warehouse_stub
```

7. simular shipping outage:

```bash
SHIPPING_STUB_MODE=outage docker compose up --build shipping_stub
```

8. observar dead-letter;
9. requeue via Operations View;
10. confirmar recovery.

## Relatorio

Se for seguro, atualizar:

- `docs/report/chapters/07-architectural-decisions.tex`
- `docs/report/chapters/10-feasibility-spike.tex`

Adicionar uma secao curta de implementacao/evidencia, sem reescrever o relatorio
todo.

Se a compilacao LaTeX for viavel:

```bash
cd docs/report
./compile.sh
```

Se nao for viavel, deixa claro no final que os `.tex` foram alterados mas o PDF
nao foi regenerado.

## Validacao tecnica

Antes de terminar, corre o maximo viavel:

```bash
dotnet build src/Integration/Nop.IntegrationWorker/Nop.IntegrationWorker.csproj
dotnet build src/NopCommerce.sln --no-incremental
docker compose config
docker compose build integration_worker
```

Teste runtime recomendado:

```bash
docker compose up --build
```

Validar:

- modo normal processa warehouse e shipping;
- modo warehouse failed gera retries;
- apos 5 falhas, circuit abre;
- durante circuit open, chamadas ao adapter param;
- apos cooldown/probe, circuit fecha em recovery;
- apos 10 tentativas, cria `DeadLetterRecord`;
- Operations View mostra dead-letter e circuit state;
- requeue volta a processar quando dependency esta healthy.

Se os delays de 5 minutos tornarem a demo lenta, adiciona configuracao por env
vars para demo, mas mantem defaults arquiteturais:

- `Resilience__InitialDelaySeconds=2`
- `Resilience__MaxDelaySeconds=300`
- `Resilience__MaxRetryAttempts=10`
- `Resilience__CircuitFailureThreshold=5`
- `Resilience__CircuitCooldownSeconds=300`

No demo script, podes usar valores menores via env var, desde que documentes que
sao valores acelerados para demonstracao.

## Regras importantes

- Nao alterar contratos RabbitMQ da Pessoa 1.
- Nao mover chamadas externas para o checkout.
- Nao remover idempotencia da Pessoa 3.
- Nao apagar outbox original quando cria dead-letter.
- Nao fazer refactor grande do nopCommerce fora da area de integracao.
- Preserva alteracoes existentes do utilizador.
- Usa `apply_patch` para edicoes manuais.
- No fim, responde em portugues com:
  - ficheiros alterados/criados;
  - politicas de retry/circuit breaker implementadas;
  - como dead-letter e requeue funcionam;
  - onde estao evidencias/demo script;
  - comandos de validacao executados e resultado;
  - limitacoes restantes.
