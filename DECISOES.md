# Decisões Técnicas

## 1. Modelagem do saldo

Foi adotada uma abordagem híbrida:

- o saldo atual é materializado em `Account.Balance`;
- o histórico financeiro é mantido em `LedgerEntry`.

A decisão de materializar o saldo evita calcular o saldo da conta somando todos os lançamentos a cada consulta. Isso é especialmente importante para uma conta que possa possuir milhões de lançamentos.

O ledger permanece como histórico financeiro e base de auditoria.

Uma transferência altera o saldo das contas e cria seus respectivos lançamentos dentro da mesma transação do banco. Dessa forma, a alteração de saldo e os registros financeiros são persistidos como uma única unidade atômica.

### Trade-off

A principal vantagem é desempenho nas consultas de saldo.

O custo é a necessidade de garantir que o saldo materializado e o histórico não sejam alterados de forma independente.

A estratégia adotada é concentrar a atualização de saldo e a criação dos lançamentos na mesma operação transacional do PostgreSQL.

A consistência também é verificada pelos testes de integração, principalmente nos cenários concorrentes.

---

## 2. Concorrência

Foi adotado **controle otimista de concorrência baseado em versão da linha**.

A entidade `Account` possui uma propriedade `Version`, incrementada quando o saldo é alterado.

O fluxo de uma transferência é:

1. iniciar transação;
2. carregar as contas envolvidas;
3. verificar saldo;
4. alterar os saldos;
5. incrementar as versões;
6. criar a transferência;
7. criar os lançamentos do ledger;
8. registrar a operação de idempotência;
9. executar `SaveChanges`;
10. confirmar a transação.

Se outra transação tiver alterado uma das mesmas contas antes da persistência, o Entity Framework Core pode detectar a alteração concorrente através do controle de versão.

Nesse cenário a operação é tratada como conflito de concorrência e pode ser repetida conforme a estratégia implementada no serviço.

### Por que essa estratégia

A aplicação não utiliza `lock` em memória.

Um `lock` protegeria apenas o processo atual e não resolveria o problema quando a API estivesse executando em duas ou mais instâncias.

O controle de concorrência fica apoiado no banco de dados, portanto continua válido independentemente da quantidade de instâncias da API.

### Custo

O custo é a possibilidade de uma operação precisar ser repetida quando houver contenção sobre a mesma conta.

Isso aumenta a latência de algumas requisições sob carga, mas evita corrupção de saldo.

Também existe possibilidade de aumento de conflitos quando muitas operações concorrentes tentam modificar as mesmas contas.

O teste de integração utiliza PostgreSQL real através do Testcontainers e comprova que transferências concorrentes não conseguem produzir saldo negativo.

---

## 3. Idempotência

O endpoint `POST /transfers` exige o header:

```text
Idempotency-Key
```

A chave é armazenada na tabela de idempotência juntamente com:

- conta de origem;
- hash da requisição;
- código HTTP da resposta;
- corpo da resposta;
- data de criação.

O hash é calculado a partir dos dados relevantes da operação de transferência.

### Mesma chave e mesmo corpo

A segunda requisição encontra o registro existente e retorna a resposta originalmente armazenada.

Nenhuma segunda transferência é criada.

### Mesma chave e corpo diferente

A chave não pode ser reutilizada para representar outra operação.

Nesse caso é retornado:

```text
409 Conflict
```

com o código:

```text
IDEMPOTENCY_KEY_REUSED
```

### Chaves diferentes

Duas requisições com o mesmo corpo, mas chaves diferentes, representam operações diferentes.

Portanto não são deduplicadas.

### Requisições simultâneas

A idempotência não depende de uma consulta prévia seguida de uma decisão em memória.

A chave é persistida no banco dentro da mesma transação da transferência.

Dessa forma, a unicidade é garantida no nível persistente e continua válida quando duas requisições chegam simultaneamente ou quando a aplicação possui múltiplas instâncias.

Os testes de integração cobrem os cenários de idempotência e concorrência.

### Retenção

A implementação atual persiste a chave juntamente com sua resposta.

A política definitiva de expiração/limpeza das chaves ainda deverá ser definida antes da utilização em produção.

---

## 4. Ledger e histórico

O histórico financeiro não deve ser apagado ou sobrescrito para representar uma nova operação.

Uma transferência cria lançamentos próprios.

Quando uma operação de estorno é realizada, a intenção é criar novos lançamentos compensatórios em vez de modificar os lançamentos originais.

Isso permite reconstruir a sequência de eventos financeiros e facilita auditoria posterior.

---

## 5. Banco de dados

Foi escolhido PostgreSQL.

Os testes de integração utilizam PostgreSQL real através do Testcontainers, em vez de banco em memória.

Essa decisão é importante porque os comportamentos de transação, concorrência, constraints e persistência são parte fundamental do desafio.

O Entity Framework Core é utilizado como ORM.

As alterações estruturais do banco são controladas por migrations.

---

## 6. Testes

A estratégia de testes prioriza testes de comportamento.

Os testes de integração executam contra um banco PostgreSQL real em container.

Isso permite validar cenários que não seriam adequadamente comprovados por mocks, principalmente:

- concorrência;
- transações;
- idempotência;
- persistência do ledger;
- consistência de saldo.

A suíte de concorrência já demonstrou o comportamento esperado para transferências simultâneas.

A suíte de idempotência também cobre reutilização de chave e requisições concorrentes.

---

## 7. O que ficou de fora / ainda está em desenvolvimento

Neste momento alguns requisitos do desafio ainda estão sendo implementados ou precisam de testes adicionais:

- estorno com suíte completa de integração;
- regra definitiva para estorno quando o saldo da conta destino já foi gasto;
- extrato com filtro por período;
- paginação;
- saldo de abertura;
- saldo de fechamento;
- revisão dos índices para grandes volumes;
- limite diário;
- padronização completa em `ProblemDetails`;
- health check;
- correlation ID;
- ampliação dos testes unitários;
- `docker compose` para subir toda a solução em um comando;
- política de retenção/limpeza de registros de idempotência.

A decisão de não implementar esses pontos superficialmente antes dos requisitos centrais está alinhada ao objetivo do teste: priorizar correção de concorrência e idempotência.

---

## 8. Dependência com vulnerabilidade conhecida

O projeto de testes possui uma dependência transitiva em:

```text
SSH.NET 2025.1.0
```

O NuGet reporta o aviso `NU1903`.

A dependência está no projeto de testes de integração e não foi introduzida como parte da lógica da API.

A decisão atual é manter a dependência temporariamente para não interromper o desenvolvimento e registrar explicitamente o risco.

Antes de uma entrega produtiva, a dependência deverá ser atualizada ou substituída por uma versão sem a vulnerabilidade aplicável.

---

## 9. Uso de IA

IA foi utilizada como ferramenta de apoio no desenvolvimento, principalmente para análise de requisitos, revisão de implementação, discussão de estratégias de concorrência/idempotência, elaboração de testes e documentação.

A implementação foi executada e validada localmente.

As decisões arquiteturais são de responsabilidade do autor e devem ser defendidas tecnicamente durante a avaliação.
