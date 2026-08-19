# Banking API

API REST para gestão de contas bancárias e transferências, desenvolvida em C# com ASP.NET Core e PostgreSQL.

O projeto foi construído com foco em **correção sob condições reais**, principalmente concorrência, idempotência e consistência entre saldo e histórico financeiro.

> **Status:** em desenvolvimento. Os itens já implementados e comprovados por testes estão marcados como concluídos; os demais permanecem explicitamente como pendentes.

---

## 1. Objetivo

A API implementa o domínio básico de contas bancárias:

- criação de contas;
- autenticação por e-mail e senha;
- emissão de JWT;
- consulta de saldo;
- consulta de extrato;
- transferências entre contas;
- controle de concorrência;
- idempotência de transferências;
- ledger financeiro;
- estorno de transferências;
- testes de integração utilizando PostgreSQL real via Testcontainers.

O foco do projeto não é apenas fazer a API funcionar no caminho feliz, mas garantir comportamento correto quando existem requisições concorrentes, reenvio de operações e necessidade de auditoria posterior.

---

## 2. Stack

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL 16
- xUnit
- Testcontainers
- Docker / Docker Desktop
- JWT Bearer Authentication
- `decimal` para valores monetários

---

## 3. Arquitetura

Estrutura principal:

```text
src/
└── BankingApi/
    ├── Authentication/
    ├── Controllers/
    ├── Data/
    ├── Domain/
    ├── DTOs/
    ├── Exceptions/
    ├── Migrations/
    ├── Models/
    └── Services/

tests/
├── BankingApi.UnitTests/
└── BankingApi.IntegrationTests/
```

As regras de negócio ficam principalmente nos serviços, enquanto os controllers são responsáveis pela exposição HTTP.

O acesso aos dados é feito através do Entity Framework Core.

---

## 4. Modelo financeiro

O saldo atual da conta é mantido na coluna `Account.Balance`.

O histórico financeiro é mantido separadamente através de lançamentos (`LedgerEntry`).

Uma transferência gera:

1. um lançamento de débito na conta de origem;
2. um lançamento de crédito na conta de destino;
3. uma entidade `Transfer`;
4. atualização do saldo das contas.

Essas alterações são persistidas dentro da mesma transação do banco.

A estratégia e seus trade-offs estão detalhados em [`DECISOES.md`](DECISOES.md).

---

## 5. Endpoints

### Contas

```http
POST /accounts
GET /accounts/me/balance
GET /accounts/me/statement
```

### Autenticação

```http
POST /auth/login
```

### Transferências

```http
POST /transfers
POST /transfers/{transferId}/reversal
```

---

## 6. Autenticação

As operações protegidas utilizam JWT Bearer.

O identificador da conta autenticada é obtido a partir do claim:

```text
account_id
```

Exemplo conceitual:

```http
Authorization: Bearer <JWT>
```

---

## 7. Transferências

Uma transferência possui:

- conta de origem;
- conta de destino;
- valor;
- status;
- data/hora;
- lançamentos correspondentes no ledger.

Regras implementadas incluem:

- valor maior que zero;
- conta de destino obrigatória;
- conta de destino existente;
- não permitir transferência para a própria conta;
- saldo suficiente;
- valor monetário representado como `decimal`;
- operação atômica;
- proteção contra concorrência;
- idempotência.

---

# 8. Concorrência

A aplicação utiliza **controle otimista de concorrência com versão da entidade `Account`**.

Cada conta possui uma versão que é incrementada quando seu saldo é alterado.

Em uma situação como:

```text
Saldo = R$ 100,00

Transferência A: R$ 60,00
Transferência B: R$ 60,00
```

as duas requisições podem inicialmente ler o mesmo saldo, mas somente uma consegue persistir a alteração correspondente à versão esperada.

A segunda operação recebe conflito de concorrência e é tratada como falha/retry conforme a implementação do serviço.

Não é utilizado:

```csharp
lock(...)
```

como mecanismo de concorrência, pois esse mecanismo seria limitado à instância atual da aplicação e não resolveria o problema quando existissem múltiplas instâncias.

## Teste de concorrência

Existe teste de integração contra PostgreSQL real utilizando Testcontainers.

O teste dispara transferências concorrentes contra uma mesma conta e verifica que:

- o saldo não fica negativo;
- somente a quantidade de transferências suportada pelo saldo é concluída;
- as demais falham;
- o resultado final permanece consistente.

O teste de concorrência já foi executado com sucesso.

---

# 9. Idempotência

`POST /transfers` exige o header:

```http
Idempotency-Key: <chave>
```

A chave é associada à conta de origem e ao hash do conteúdo relevante da requisição.

O comportamento esperado é:

### Mesma chave + mesmo corpo

Retorna a resposta original sem criar uma nova transferência.

### Mesma chave + corpo diferente

Retorna conflito:

```text
409 Conflict
```

com o código:

```text
IDEMPOTENCY_KEY_REUSED
```

### Chaves diferentes + mesmo corpo

São operações independentes e podem gerar transferências distintas.

### Requisições simultâneas

A proteção depende da persistência da chave no banco e da restrição transacional, e não de uma variável ou estrutura em memória.

Os testes de idempotência, inclusive o cenário de concorrência, já foram executados com sucesso.

---

# 10. Ledger

O projeto mantém um histórico de lançamentos financeiros.

Uma transferência cria lançamentos independentes para débito e crédito.

A intenção é manter o histórico financeiro como fonte de evidência para auditoria, evitando apagar ou modificar lançamentos históricos para representar operações posteriores.

O estorno, quando concluído, deverá gerar novos lançamentos em vez de alterar os lançamentos originais.

---

# 11. Estorno

O endpoint previsto é:

```http
POST /transfers/{transferId}/reversal
```

A funcionalidade de estorno faz parte do escopo obrigatório do desafio.

Regras esperadas:

- uma transferência só pode ser estornada uma vez;
- o lançamento original permanece intacto;
- o estorno gera novos lançamentos;
- o saldo é atualizado de forma atômica;
- o histórico permanece auditável.

Os testes específicos de integração do estorno ainda estão sendo finalizados.

---

# 12. Testes

## Testes unitários

Projeto:

```text
tests/BankingApi.UnitTests
```

Os testes unitários serão utilizados para validar regras de negócio isoladas.

## Testes de integração

Projeto:

```text
tests/BankingApi.IntegrationTests
```

Os testes utilizam PostgreSQL real através de Testcontainers.

Isso é importante porque os requisitos de concorrência e transação dependem do comportamento real do banco relacional.

### Executar todos os testes

```powershell
dotnet test .\BankingApi.slnx
```

### Executar somente os testes de integração

```powershell
dotnet test .\tests\BankingApi.IntegrationTests\BankingApi.IntegrationTests.csproj
```

### Executar o teste de concorrência

```powershell
dotnet test .\tests\BankingApi.IntegrationTests\BankingApi.IntegrationTests.csproj --filter "FullyQualifiedName~ConcurrentTransfers_ShouldNeverCreateNegativeBalance"
```

O Docker Desktop precisa estar disponível para os testes que utilizam Testcontainers.

---

# 13. PostgreSQL e Testcontainers

Os testes de integração sobem um PostgreSQL real em container.

Durante a execução observada do projeto:

```text
PostgreSQL 16-alpine
Docker Desktop
Testcontainers 4.13.0
```

O container é criado para o teste e removido ao final da execução.

Isso evita depender de um banco PostgreSQL instalado localmente para os testes.

---

# 14. Banco de dados

As alterações do modelo são controladas através de migrations do Entity Framework Core.

Migrations atualmente existentes incluem:

```text
20260816214133_InitialCreate
20260817025347_AddLedgerEntries
20260817165934_AddTransferReversal
```

---

# 15. Erros

As regras de negócio utilizam exceções de domínio (`BusinessException`) com código de erro estável.

Exemplos:

```text
IDEMPOTENCY_KEY_REQUIRED
IDEMPOTENCY_KEY_REUSED
INVALID_AMOUNT
DESTINATION_ACCOUNT_REQUIRED
SOURCE_ACCOUNT_NOT_FOUND
DESTINATION_ACCOUNT_NOT_FOUND
SELF_TRANSFER_NOT_ALLOWED
INSUFFICIENT_FUNDS
CONCURRENT_MODIFICATION
```

A camada de tratamento de exceções converte esses erros em respostas HTTP apropriadas.

---

# 16. Aviso de dependência

No momento, o projeto apresenta um aviso de segurança referente ao pacote:

```text
SSH.NET 2025.1.0
```

O aviso é:

```text
NU1903
```

O pacote está sendo utilizado no projeto de testes de integração como dependência transitiva.

A decisão atual é manter o pacote temporariamente para não interromper o desenvolvimento do desafio, registrando o risco para tratamento posterior.

Esse aviso não impediu o build nem os testes atuais.

---

# 17. Status dos requisitos do desafio

| Requisito | Status |
|---|---|
| `POST /accounts` | ✅ |
| `POST /auth/login` | ✅ |
| `GET /accounts/me/balance` | ✅ |
| `GET /accounts/me/statement` | 🔄 |
| `POST /transfers` | ✅ |
| Entrada de dinheiro | ✅ |
| Hash de senha | ✅ |
| Valores em `decimal` | ✅ |
| Transferência atômica | ✅ |
| Saldo não negativo | ✅ Testado |
| Concorrência | ✅ Testado |
| Idempotência | ✅ Testado |
| Estorno | 🔄 |
| Histórico imutável | 🔄/parcial |
| Extrato com período | 🔄 |
| Paginação do extrato | 🔄 |
| Saldo de abertura/fechamento | 🔄 |
| Índice para extrato | 🔄 |
| ProblemDetails/RFC 7807 | 🔄 |
| Limite diário | 🔄 |
| Testes unitários de regras | 🔄 |
| Testes de integração | ✅ Parcial |
| PostgreSQL real | ✅ |
| Testcontainers | ✅ |
| Health check | 🔄 |
| Correlation ID | 🔄 |
| `DECISOES.md` | ✅ |
| `README.md` | ✅ |

---

# 18. Como executar

## Pré-requisitos

- .NET 10 SDK
- Docker Desktop
- Docker Engine disponível para os testes de integração
- PostgreSQL para execução da aplicação, conforme configuração do ambiente

## Build

```powershell
dotnet build .\BankingApi.slnx
```

## Testes

```powershell
dotnet test .\BankingApi.slnx
```

## Executar a API

A partir da raiz:

```powershell
dotnet run --project .\src\BankingApi\BankingApi.csproj
```

---

# 19. Exemplos

## Criar conta

```http
POST /accounts
Content-Type: application/json

{
  "name": "Thiago",
  "email": "thiago@example.com",
  "password": "SenhaForte123!"
}
```

## Login

```http
POST /auth/login
Content-Type: application/json

{
  "email": "thiago@example.com",
  "password": "SenhaForte123!"
}
```

## Transferência

```http
POST /transfers
Authorization: Bearer <JWT>
Idempotency-Key: 8f5f7a8e-7f70-4c61-ae5a-123456789abc
Content-Type: application/json

{
  "destinationAccountNumber": "12345678",
  "amount": 50.00
}
```

## Estorno

```http
POST /transfers/{transferId}/reversal
Authorization: Bearer <JWT>
```

---

# 20. Decisões técnicas

As principais decisões de arquitetura, concorrência, idempotência e modelagem financeira estão documentadas em:

```text
DECISOES.md
```

---

# 21. Uso de inteligência artificial

A inteligência artificial foi utilizada como ferramenta de apoio durante o desenvolvimento, principalmente para:

- análise de requisitos;
- discussão de alternativas de arquitetura;
- revisão de código;
- elaboração e evolução de testes;
- identificação de problemas de concorrência e idempotência;
- documentação técnica.

As decisões finais, implementação e testes foram revisados e executados no ambiente local do projeto.

O código entregue deve ser defendido tecnicamente pelo autor durante a avaliação.

---

# 22. Próximos passos

Os próximos itens de maior prioridade são:

1. concluir e testar estorno;
2. finalizar extrato com período e paginação;
3. garantir saldo de abertura e fechamento;
4. revisar índices do banco;
5. implementar limite diário;
6. padronizar completamente os erros em ProblemDetails;
7. adicionar health check;
8. adicionar correlation ID;
9. completar testes unitários;
10. finalizar `docker compose`;
11. executar a suíte completa;
12. revisar documentação antes da publicação.
