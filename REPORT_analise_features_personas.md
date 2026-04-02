# Relatório de Análise — User Features ECS (Personas 2.0)

**Data**: 2026-04-02  
**Escopo**: Revisão crítica do notebook de engenharia de features comportamentais e financeiras para segmentação de personas, cobrindo arquitetura, consultas SQL, escolha de features, consistência documental e correções necessárias.

---

## 1. Visão Geral e Arquitetura

A abordagem adotada é **tecnicamente sólida**:

- **TEMP VIEWs por domínio** com single-pass e conditional aggregation é a melhor prática para Spark SQL — evita múltiplos scans da mesma tabela e permite ao Catalyst optimizer paralelizar leituras.
- **LEFT JOIN chain** à spine preserva 100% da base (~93,5M) e transforma a ausência de dados em NULLs semanticamente corretos.
- **Janelas temporais cumulativas** (30d ⊂ 90d ⊂ 180d ⊂ 360d) são padrão na indústria para feature engineering comportamental.
- **Materialização em tabela Delta** com `CREATE OR REPLACE TABLE` é adequada para um snapshot de features.
- **Validação de fontes** antes do processamento pesado é uma boa prática de engenharia de dados.

Nenhuma alteração arquitetural é necessária. As observações a seguir são sobre problemas pontuais, inconsistências e oportunidades de melhoria.

---

## 2. Bugs e Erros Encontrados

### 2.1 BUG — `ft_premium_tenure_days` usa MIN em vez de MAX

**Gravidade: Alta — produz valor incorreto.**

```sql
MIN(DATEDIFF('${pipeline.dt_ref}', created_at)) AS ft_premium_tenure_days
```

`DATEDIFF(DT_REF, created_at)` retorna o número de dias desde a criação da assinatura. Para a assinatura **mais antiga**, o DATEDIFF é **maior**. Portanto:

- `MIN(DATEDIFF(...))` = menor diferença = assinatura **mais recente** (tenure mais curta)
- `MAX(DATEDIFF(...))` = maior diferença = assinatura **mais antiga** (tenure mais longa)

A documentação diz explicitamente: *"MIN(tenure) para capturar... a assinatura mais longa"* — mas isso é contraditório. MIN retorna a **mais curta**, não a mais longa.

**Correção**: Decidir a semântica desejada:
- Se quer "há quanto tempo é Premium" (assinatura mais antiga): trocar para `MAX`
- Se quer "tenure da assinatura mais recente": manter `MIN` mas **corrigir a documentação**

**Recomendação**: Para personas, o mais informativo é `MAX(DATEDIFF(...))` — captura "há quanto tempo o usuário está no ecossistema Premium", que é o sinal mais forte de fidelidade.

---

### 2.2 ERRO CONCEITUAL — Trend Ratio documentado como podendo ser > 1

**Gravidade: Média — a feature funciona, mas a interpretação está errada.**

A documentação afirma em múltiplos locais:

> *`ft_auth_trend_ratio`: count_30d / count_360d → **>1 = acelerando**, <1 = desacelerando*

Isso é **matematicamente impossível**. Como a janela de 30 dias é um **subconjunto** da janela de 360 dias, o numerador nunca pode exceder o denominador:

```
ft_auth_login_count_30d ≤ ft_auth_login_count_360d  (sempre)
∴ ft_auth_trend_ratio ≤ 1.0  (sempre)
```

**Interpretação correta**: O ponto neutro (atividade uniformemente distribuída) é:

```
ratio_neutro = 30 / 360 ≈ 0.083
```

- `ratio > 0.083` → atividade **concentrada no período recente** (acelerando)
- `ratio ≈ 0.083` → atividade **uniforme** ao longo do ano
- `ratio < 0.083` → atividade **concentrada em períodos antigos** (desacelerando)
- `ratio = 1.0` → **toda** a atividade ocorreu nos últimos 30 dias
- `ratio → 0` → nenhuma atividade recente (dormante)

**Alternativa** (se quiser um ratio cuja neutralidade seja 1.0):

```sql
(count_30d * 12.0) / NULLIF(count_360d, 0) AS ft_auth_trend_ratio
```

Esse ratio normalizado multiplicado por 12 (= 360/30) faz com que `>1 = acelerando` e `<1 = desacelerando` sejam literalmente corretos. Contudo, para fins de clusterização (PCA/KMeans), ambas as formulações produzem a mesma ordenação relativa — a escolha é mais de interpretabilidade.

**Ação mínima**: Corrigir a documentação. **Ação recomendada**: Considerar usar o ratio normalizado para facilitar a interpretação dos clusters.

Este mesmo erro se aplica a **todos** os trend ratios: `ft_auth_trend_ratio`, `ft_lno_trend_ratio`, `ft_ecred_trend_ratio`, `ft_contas_trend_ratio`.

---

### 2.3 ERRO DE ROTULAÇÃO — Labels "HRM4" na validação de cobertura

**Gravidade: Baixa — não afeta dados, apenas confunde a documentação.**

Na query de validação (UNPIVOT):

```sql
c_renda           AS `Renda (HRM4)`,
c_capacidade      AS `Capacidade (HRM4)`,
c_comprometimento AS `Comprometimento (HRM4)`
```

As variáveis reais são **HRP2**, **HCPA** e **HCOR** — "HRM4" não corresponde a nenhuma delas. Parece ser um nome de projeto/modelo antigo que ficou no código.

**Correção**:

```sql
c_renda           AS `Renda (HRP2)`,
c_capacidade      AS `Capacidade (HCPA)`,
c_comprometimento AS `Comprometimento (HCOR)`
```

---

## 3. Inconsistências entre Documentação e Código

### 3.1 Catálogo de Features incompleto

O catálogo no topo do notebook **não lista** várias features que são efetivamente computadas e materializadas. Features presentes no código mas ausentes do catálogo:

| Feature ausente do catálogo | Domínio | Presente no SQL |
|---|---|---|
| `ft_lno_active_days_{w}d` | LNO | Sim, na TEMP VIEW e no SELECT final |
| `ft_ecred_sim_recency_days` | eCred Sim | Sim |
| `ft_ecred_ord_recency_days` | eCred Ord | Sim |
| `ft_ecred_sim_rate_{w}d` | eCred Sim | Sim, no SELECT final |
| `ft_contas_active_days_{w}d` | Minhas Contas | **Não** (não computado — ver seção 4.1) |

**Ação**: Atualizar o catálogo de features no topo do notebook para refletir todas as features efetivamente materializadas.

### 3.2 Coluna "Janelas" inconsistente para LNO

No catálogo:

```
| LNO | ft_lno_rate, ft_lno_pct_active_days, ft_lno_trend_ratio | DOUBLE | 30d / 360d |
```

- `ft_lno_rate` e `ft_lno_pct_active_days` são computados para **todas as 4 janelas** (30d, 90d, 180d, 360d) no SQL final
- Apenas `ft_lno_trend_ratio` usa a fórmula "30d / 360d"

A coluna "Janelas" está misturando a **lista de janelas disponíveis** com a **fórmula do trend ratio**. Compare com Auth, que lista corretamente "30d, 90d, 180d, 360d" para rate e pct_active_days.

**Correção**: Separar as linhas no catálogo — rates com "30d, 90d, 180d, 360d" e trend_ratio com "—".

### 3.3 Tipos no catálogo não correspondem ao SQL

O catálogo lista `ft_ecred_sim_count` como **DOUBLE**, mas `COUNT_IF()` retorna **BIGINT** no Spark SQL. Da mesma forma, `ft_contas_events` e `ft_contas_payments` estão como DOUBLE no catálogo, mas `COUNT_IF` e `SUM(CASE ... THEN 1 ELSE 0 END)` retornam inteiros.

Isso não afeta a pipeline (Spark faz cast implícito em JOINs e cálculos), mas para documentação precisa, os tipos devem refletir o output real.

---

## 4. Lacunas de Design e Features Ausentes

### 4.1 Minhas Contas: Falta `ft_contas_active_days_{w}d`

Auth e LNO computam `active_days` (dias distintos com interação) e derivam `pct_active_days` (constância de uso). **Minhas Contas não tem nenhum dos dois**, apesar de ter 53% de cobertura — o suficiente para ser uma feature discriminadora.

A TEMP VIEW `ft_contas` computa `events`, `payments` e `total_value`, mas não `active_days`.

**Recomendação**: Adicionar à TEMP VIEW `ft_contas`:

```sql
COUNT(DISTINCT CASE WHEN dt_event >= '${pipeline.dt_30d}' THEN dt_event END) AS ft_contas_active_days_30d,
-- (análogo para 90d, 180d, 360d)
```

E no SELECT final, derivar `ft_contas_pct_active_days_{w}d` e adicionar `ft_contas_rate_{w}d` (events / LEAST(age, w)) para paridade com Auth e LNO.

### 4.2 Seguro: Falta trend_ratio e rate

Todos os domínios de "massa" (Auth, LNO, eCred, Contas) têm trend_ratio. Seguro não tem. Dado o volume ultra-baixo (0,05%), isso é justificável — poucos usuários teriam dados suficientes para um trend_ratio significativo. **Não é crítico, mas vale documentar a decisão.**

### 4.3 Scores: Estratégia de dedup inconsistente

- **HRP3** usa `QUALIFY ROW_NUMBER() OVER (PARTITION BY cd_uuid ORDER BY dt_ref DESC) = 1` — **dedup per-user** (pega o registro mais recente de cada usuário, independentemente do batch).
- **HSV4, HRLC, HRP2, HCPA, HCOR** usam `WHERE dt_ref = (SELECT MAX(dt_ref) ...)` — **dedup por batch** (pega todos os registros do lote mais recente).

A diferença prática:
- Se um usuário **não aparece no batch mais recente** mas tem registro em batch anterior, a abordagem per-batch perde esse usuário, enquanto a per-user retém.
- Se o batch mais recente cobre ~100% dos CPFs (como sugere a cobertura de ~100% na validação), ambas as abordagens são equivalentes.

**Risco**: Se em uma futura execução o batch mais recente estiver incompleto (ex: falha parcial de carga), HSV4/HRLC/indicadores perderão cobertura silenciosamente. A abordagem per-user (como HRP3) é mais robusta.

**Recomendação**: Padronizar para per-user dedup em todas as tabelas de score. Caso o batch-level seja mantido, documentar a premissa de que o batch mais recente é sempre completo.

### 4.4 Premium: Escopo temporal diferente dos demais domínios

As features comportamentais (Auth, LNO, eCred, Contas, Seguro) são filtradas pela janela de 360d. Premium **não tem filtro temporal** — captura qualquer assinatura, independentemente de quando foi criada.

Isso significa que `fl_has_premium = 1` pode incluir assinaturas de 5 anos atrás já canceladas. Os demais `fl_has_*` são limitados a atividade nos últimos 360 dias.

**Impacto**: Na contagem `ft_n_products_used`, Premium infla o número para usuários que tiveram assinatura no passado distante mas estão inativos hoje. Esses usuários podem ter `fl_has_premium = 1` mas `ft_premium_is_active = 0`.

**Recomendação**: Isso é aceitável SE o objetivo é capturar "já foi Premium alguma vez" (perfil financeiro). Se o objetivo é "engajamento ativo", considerar adicionar um filtro temporal ou usar `ft_premium_is_active` como critério para o flag.

### 4.5 Mapping `user_id` → `cd_uuid` (Premium e Seguro)

Duas tabelas (Premium e Seguro) fazem join por `user_id` em vez de `cd_uuid`. O código faz `user_id AS cd_uuid`, assumindo que são equivalentes.

A spine tem duas colunas de identidade: `user_id_canonico` e `cd_uuid`. Se `user_id` nas tabelas de Premium/Seguro corresponde a `user_id_canonico` (e não a `cd_uuid`), o LEFT JOIN falhará silenciosamente — os registros não vão casar, e esses domínios terão cobertura próxima de zero.

**Dado que Premium tem 2,79% de cobertura e Seguro 0,05%, os números parecem plausíveis** (consistentes com o tamanho esperado desses nichos). Porém, vale validar com uma query de sanidade:

```sql
SELECT COUNT(DISTINCT p.user_id) AS total_premium,
       COUNT(DISTINCT s.cd_uuid) AS matched_spine
FROM premium_prd.silver... p
LEFT JOIN mcp_prd.sandbox.user_spine_v0 s ON s.cd_uuid = p.user_id
```

Se `matched_spine` << `total_premium`, o mapping está errado.

---

## 5. Análise da Escolha de Features

### 5.1 Features que fazem sentido e estão bem modeladas

| Feature | Avaliação |
|---|---|
| `ft_account_age_days` + flags de maturidade | Excelente. Discretizar idade em buckets (onboarding/early/established/mature) é padrão e útil para clusterização. |
| `ft_auth_*` (login count, platforms, active_days) | Sólido. Captura três dimensões de engajamento digital: volume, diversidade e constância. |
| `ft_auth_rate_{w}d` e `ft_auth_pct_active_days_{w}d` | Excelente. Normalização por LEAST(age, window) elimina o viés pró-contas antigas. NULLIF(..., 0) protege contra divisão por zero. |
| `ft_lno_agreements_{w}d` | Relevante. Acordos fechados ≠ eventos totais — captura a *efetivação* da negociação, não apenas a visualização. |
| `ft_ecred_sim_*` vs `ft_ecred_ord_*` | Excelente design. Separar simulação de pedido revela o funil de crédito. A conversion_rate é uma feature derivada valiosa. |
| `ft_ecred_ord_conversion_rate_{w}d` | Bem modelada. `contracted / total_orders` é a definição correta de taxa de conversão no contexto. |
| `fl_has_*` + `ft_n_products_used` | Boa abordagem. Flags binários de atividade por domínio + contagem multi-produto são features discriminadoras clássicas em segmentação. |
| Scores (HRP3, HSV4, HRLC) | Adequados. Score de crédito, B2B e consumidor cobrem os três eixos de risco/perfil. |
| Indicadores financeiros (HRP2, HCPA, HCOR) | Complementam bem os scores. Renda + Capacidade + Comprometimento formam um tripé financeiro completo. |

### 5.2 Features que merecem revisão ou atenção

| Feature | Observação |
|---|---|
| `ft_ecred_trend_ratio` (combinado) | Soma sim + ord no numerador e denominador. Isso mascara cenários onde simulações aceleram mas pedidos desaceleram (ou vice-versa). **Considerar**: trend ratios separados para sim e ord. |
| `ft_premium_is_suspended` | Com cobertura de 2,79%, e dentro disso apenas uma fração está suspensa, essa feature terá variância mínima. Útil como flag mas improvável que contribua para separação de clusters. Não é crítico — PCA/KMeans lidam bem com features de baixa variância. |
| `ft_contas_total_value_{w}d` | SUM de valores sem normalização. Usuários com muitos pagamentos pequenos e poucos pagamentos grandes terão valores parecidos. **Considerar**: `ft_contas_avg_value_{w}d` = total_value / payments (ticket médio), que é mais discriminador para perfil financeiro. |
| `ft_ecred_sim_max_value_{w}d` | O valor máximo simulado é uma proxy de "aspiração de crédito". Faz sentido. Mas se `vl_simulation` contém outliers extremos (ex: R$ 999.999), pode distorcer clusters. Avaliar na análise exploratória. |

### 5.3 Features que poderiam ser adicionadas (sugestões para próxima iteração)

Estas são sugestões, **não são problemas**. Feature engineering é iterativo.

| Feature sugerida | Fonte | Justificativa |
|---|---|---|
| `ft_lno_agreement_rate_{w}d` | LNO | `agreements / events` — taxa de conversão dentro do LNO, análogo ao conversion_rate do eCred. |
| `ft_contas_avg_value_{w}d` | Minhas Contas | `total_value / payments` — ticket médio de pagamento, discrimina perfil financeiro. |
| `ft_ecred_sim_distinct_products_{w}d` | eCred Sim | Tipos de crédito distintos simulados — diversidade de interesse creditício. |
| `fl_auth_active_lno` | Cross | Flag indicando se o usuário tem TANTO login quanto atividade LNO — distingue LNO ativo vs passivo. |
| `ft_auth_weekend_ratio` | Auth | Proporção de logins em fim de semana vs total — sinal comportamental de perfil. |

---

## 6. Análise das Consultas SQL

### 6.1 Consultas corretas e bem otimizadas

- **Todas as TEMP VIEWs** (Auth, LNO, eCred Sim, eCred Ord, Contas, Seguro): Padrão single-pass com conditional aggregation está correto. Os filtros WHERE limitam ao max_window (360d) e as janelas menores são aplicadas via CASE WHEN/COUNT_IF.

- **FULL OUTER JOIN nos scores**: O encadeamento com `COALESCE(h.cd_uuid, v.cd_uuid)` para a chave do segundo join é correto — garante que todos os cd_uuids de qualquer score apareçam.

- **Consolidação final**: O LEFT JOIN chain é correto e bem ordenado. Features derivadas são computadas inline sem scans adicionais.

- **Validação de cobertura**: UNPIVOT é uma solução elegante e eficiente para single-pass validation.

### 6.2 Ponto de atenção: `dt_load` no eCred Ord

A coluna de data usada é `dt_load` (data de carga/carregamento), não uma data de criação do pedido. Se houver delay entre o pedido real e a carga no data lake, os pedidos serão atribuídos a janelas temporais incorretas.

**Exemplo**: Um pedido feito em 28/02 mas carregado em 05/03 seria contado na janela de 30 dias quando a referência é 30/03, mas não deveria ser se a janela de 30 dias termina em 28/02.

**Recomendação**: Verificar se a tabela `tb_order_report` possui uma coluna como `dt_order` ou `dt_created` que reflita a data real do pedido. Se existir, usar essa coluna.

### 6.3 Ponto de atenção: Seguro — `TO_DATE(date)` no WHERE

```sql
WHERE TO_DATE(date) >= '${pipeline.dt_360d}'
```

Aplicar `TO_DATE()` na coluna do WHERE impede partition pruning se a tabela é particionada pela coluna `date`. Para tabelas grandes, isso pode ter impacto de performance. Como Seguro tem pouquíssimos dados (42K usuários), o impacto prático é negligível neste caso.

---

## 7. Análise Cross-Domain (Login × Produto)

A análise de cruzamento Auth × Produto é **excelente** e revela insights genuínos:

### 7.1 Achados válidos

- **25,59% (23.9M) com produto mas sem Auth**: Corretamente explicado por eventos passivos de LNO (dívidas registradas por credores).
- **85,69% desse segmento é LNO**: Confirma a hipótese de eventos passivos.
- **0,02% "window shoppers"**: Corretamente identificado como irrelevante.
- **6.4M com eCred sem login**: Flagged corretamente como surpreendente — merece investigação.

### 7.2 Recomendação sobre o segmento "Produto sem Auth"

A sugestão de considerar `WHERE ft_n_products_used >= 1` para filtrar 11.3M de usuários sem interação é **razoável**, mas com ressalva:

- Esses 11.3M **possuem** scores e indicadores financeiros (HRP3, HSV4, HRLC, HRP2, HCPA, HCOR)
- Eles representam um segmento real: "CPF com score mas sem nenhum engajamento digital"
- Para fins de **ativação e targeting**, esse segmento é valioso
- Para fins de **clusterização comportamental**, são ruído (todas as features comportamentais são NULL/0)

**Recomendação**: Não filtrar a priori. Deixar o algoritmo de clusterização identificá-los como cluster separado. Se os clusters ficarem "poluídos" por esse grupo, filtrá-los em iteração posterior e rodar a clusterização apenas na subpopulação com `ft_n_products_used >= 1`.

---

## 8. Resumo de Ações

### Correções obrigatórias (antes de prosseguir)

| # | Item | Gravidade | Ação |
|---|---|---|---|
| 1 | `ft_premium_tenure_days` MIN vs MAX | Alta | Decidir a semântica e corrigir SQL ou documentação |
| 2 | Trend ratio documentado como ">1" | Média | Corrigir documentação (ponto neutro = 0.083) ou normalizar o ratio multiplicando por 12 |
| 3 | Labels "HRM4" na validação | Baixa | Trocar por HRP2/HCPA/HCOR |

### Melhorias recomendadas (não bloqueiam próxima fase)

| # | Item | Impacto |
|---|---|---|
| 4 | Adicionar `ft_contas_active_days_{w}d` e derivados | Paridade com Auth/LNO, feature útil |
| 5 | Adicionar `ft_contas_avg_value_{w}d` (ticket médio) | Feature financeira discriminadora |
| 6 | Padronizar dedup de scores para per-user (QUALIFY ROW_NUMBER) | Robustez contra batches incompletos |
| 7 | Verificar `dt_load` vs `dt_order` no eCred Ord | Precisão temporal |
| 8 | Validar mapping `user_id = cd_uuid` em Premium e Seguro | Garantir que o join está correto |
| 9 | Atualizar catálogo de features para refletir todas as features materializadas | Documentação completa |
| 10 | Considerar trend ratios separados para eCred Sim e eCred Ord | Granularidade de insights |

---

## 9. Parecer Final

O notebook está **bem construído** e demonstra domínio sólido de engenharia de features em Spark SQL. A arquitetura é eficiente, as features são majoritariamente bem escolhidas, e a análise exploratória (cross-domain) gera insights genuínos.

**Os itens #1, #2 e #3 devem ser corrigidos antes de prosseguir para a próxima fase** (seleção de features financeiras + comportamentais), pois o #1 produz dados incorretos e o #2 pode levar a interpretações erradas dos clusters. Os demais itens são melhorias incrementais que podem ser incorporadas na próxima iteração.

O trabalho está pronto para avançar para a fase de seleção de features, desde que as três correções obrigatórias sejam aplicadas. A base de ~93,5M usuários com ~130+ features (brutas + derivadas) cobrindo 6 domínios comportamentais e 6 indicadores financeiros é uma fundação robusta para a construção de personas.
