---
description: research
allowed-arguments: ["topic"]
argument-description: "Chủ đề cần research (vd: 'golang concurrency', 'react vs vue', 'kubernetes security')"
---
```mermaid
flowchart TD
    start_node([Start])
    end_node([End])
    rs_prompt_parse_input[You are starting the Resear...]
    rs_question_initial_config{AskUserQuestion:<br/>Chọn cấu hình research}
    rs_prompt_set_config_low[## Configuration Set]
    rs_prompt_set_config_medium[## Configuration Set]
    rs_prompt_set_config_high[## Configuration Set]
    rs_prompt_guide_search[## Guide Search Phase]
    rs_prompt_propose_strategy[## Proposed Research Strategy]
    rs_question_confirm_strategy{AskUserQuestion:<br/>Confirm hoặc điều chỉnh strategy?}
    rs_prompt_adjust_strategy[## Strategy Adjustment]
    rs_prompt_create_tasks[## Execute Research Strategy]
    rs_agent_research[rs-agent-research]
    rs_agent_deep_research[rs-agent-deep-research]
    rs_prompt_merge_results[## Merge Research Results]
    rs_prompt_synthesize[## Synthesize Final Report]
    rs_prompt_present_report[## Present Research Report]
    rs_question_satisfied{AskUserQuestion:<br/>Báo cáo có đủ thông tin không?}
    rs_prompt_collect_followup[## Followup Research Request]
    rs_prompt_final_output[## Research Complete]

    start_node --> rs_prompt_parse_input
    rs_prompt_parse_input --> rs_question_initial_config
    rs_question_initial_config -->|Low - Nhanh (3 queries/agent)| rs_prompt_set_config_low
    rs_question_initial_config -->|Medium - Cân bằng (5 queries/agent)| rs_prompt_set_config_medium
    rs_question_initial_config -->|High - Toàn diện (10 queries/agent)| rs_prompt_set_config_high
    rs_prompt_set_config_low --> rs_prompt_guide_search
    rs_prompt_set_config_medium --> rs_prompt_guide_search
    rs_prompt_set_config_high --> rs_prompt_guide_search
    rs_prompt_guide_search --> rs_prompt_propose_strategy
    rs_prompt_propose_strategy --> rs_question_confirm_strategy
    rs_question_confirm_strategy -->|Confirm, thực hiện| rs_prompt_create_tasks
    rs_question_confirm_strategy -->|Điều chỉnh| rs_prompt_adjust_strategy
    rs_prompt_adjust_strategy --> rs_prompt_propose_strategy
    rs_prompt_create_tasks --> rs_agent_research
    rs_prompt_create_tasks --> rs_agent_deep_research
    rs_agent_research --> rs_prompt_merge_results
    rs_agent_deep_research --> rs_prompt_merge_results
    rs_prompt_merge_results --> rs_prompt_synthesize
    rs_prompt_synthesize --> rs_prompt_present_report
    rs_prompt_present_report --> rs_question_satisfied
    rs_question_satisfied -->|Đã đủ, hoàn tất| rs_prompt_final_output
    rs_question_satisfied -->|Cần thêm thông tin| rs_prompt_collect_followup
    rs_prompt_collect_followup --> rs_prompt_guide_search
    rs_prompt_final_output --> end_node
```

## Workflow Execution Guide

Follow the Mermaid flowchart above to execute the workflow. Each node type has specific execution methods as described below.

### Execution Methods by Node Type

- **Rectangle nodes**: Execute Sub-Agents using the Task tool
- **Diamond nodes (AskUserQuestion:...)**: Use the AskUserQuestion tool to prompt the user and branch based on their response
- **Diamond nodes (Branch/Switch:...)**: Automatically branch based on the results of previous processing (see details section)
- **Rectangle nodes (Prompt nodes)**: Execute the prompts described in the details section below

### Prompt Node Details

#### rs_prompt_parse_input(You are starting the Resear...)

```
You are starting the Research workflow.

User's research topic:
{{topic}}

(If no topic argument provided, use {{input}} from conversation context)

## Your Task

Analyze this research request and identify:

1. **Core Topic**: What exactly does the user want to research?
2. **Research Type**:
   - Factual (what is X?)
   - Comparative (X vs Y?)
   - Exploratory (how to do X?)
   - Decision-making (should I use X?)
3. **Apparent Complexity**:
   - Simple (single concept)
   - Moderate (multiple related concepts)
   - Complex (system/architecture/many factors)
4. **Potential Subtopics**: What aspects might need separate investigation?

## Output
Provide a clear summary that will help configure the research strategy.

**Context to track:**
- originalInput: The user's original query
- coreTopic: Extracted main topic
- researchType: Identified type
- apparentComplexity: Simple/Moderate/Complex
```

#### rs_prompt_set_config_low(## Configuration Set)

```
## Configuration Set

**Effort Level**: Low
**Queries per Agent**: 3
**Search Strategy**: Balanced

Proceed to guide search with this configuration.
```

#### rs_prompt_set_config_medium(## Configuration Set)

```
## Configuration Set

**Effort Level**: Medium
**Queries per Agent**: 5
**Search Strategy**: Balanced

Proceed to guide search with this configuration.
```

#### rs_prompt_set_config_high(## Configuration Set)

```
## Configuration Set

**Effort Level**: High
**Queries per Agent**: 10
**Search Strategy**: Balanced

Proceed to guide search with this configuration.
```

#### rs_prompt_guide_search(## Guide Search Phase)

```
## Guide Search Phase

Perform a guide search to understand the topic landscape before proposing a research strategy.

**Research Topic**: {{coreTopic}}
**Research Type**: {{researchType}}
**User Configuration**:
- Effort Level: {{effortLevel}}
- Queries per Agent: {{queriesPerAgent}}

## Your Task

Execute 3 searches to understand the topic landscape.

### CRITICAL: PARALLEL EXECUTION
You MUST call `mcp__my-answer-engine__ask_internet` 3 times in a SINGLE message.
Do NOT wait for one search to complete before starting another.
All 3 searches run simultaneously for maximum speed.

### Search Queries (execute ALL in parallel)

| # | Query Type | Search For | Mode | Detail |
|---|------------|------------|------|--------|
| 1 | Overview | "[topic] overview" or "[topic] guide" | pro | detailed |
| 2 | Structure | "[topic] key aspects" or "[topic] components" | pro | detailed |
| 3 | Controversy | "[topic] problems" or "[topic] limitations" | pro | detailed |

## Output

After searches, provide:

### Topic Landscape

#### Key Aspects Identified
1. [Aspect 1]: [Brief description]
2. [Aspect 2]: [Brief description]
3. [Aspect 3]: [Brief description]

#### Potential Controversies/Debates
- [Controversy 1]
- [Controversy 2]

#### Complexity Assessment
- **Actual Complexity**: [Simple/Moderate/Complex/Very Complex]
- **Justification**: [Why this assessment]

#### Recommended Agent Configuration
Based on {{effortLevel}} effort level and topic complexity:

- **Research Agents**: [X] agents covering:
  1. [Angle 1]: [Mode]
  2. [Angle 2]: [Mode]

- **Deep Research Agents**: [Y] agents investigating:
  1. [Focus 1]
  2. [Focus 2]

**Total Planned Searches**: (X + Y) * {{queriesPerAgent}} = [Total]
```

#### rs_prompt_propose_strategy(## Proposed Research Strategy)

```
## Proposed Research Strategy

Based on guide search results, present the proposed strategy to the user.

**Topic**: {{coreTopic}}
**Effort Level**: {{effortLevel}}
**Queries per Agent**: {{queriesPerAgent}}

---

## Research Strategy Proposal

### Overview
- **Topic**: {{coreTopic}}
- **Complexity**: {{assessedComplexity}}
- **Effort Level**: {{effortLevel}}
- **Queries per Agent**: {{queriesPerAgent}}

### Research Agents ({{numResearchAgents}} total)
Each agent performs {{queriesPerAgent}} searches with detail_level='detailed'

| # | Assigned Angle | Search Mode | Focus |
|---|----------------|-------------|-------|
| 1 | [angle] | [mode] | [focus] |
| 2 | [angle] | [mode] | [focus] |

### Deep Research Agents ({{numDeepAgents}} total)
Each agent performs {{queriesPerAgent}} searches with detail_level='comprehensive' + source verification

| # | Assigned Focus | Search Mode | What to Investigate |
|---|----------------|-------------|---------------------|
| 1 | [focus] | [mode] | [investigation] |

### Search Budget
- Research Agents: {{numResearchAgents}} × {{queriesPerAgent}} = {{researchSearches}} searches
- Deep Research Agents: {{numDeepAgents}} × {{queriesPerAgent}} = {{deepSearches}} searches
- **Total**: {{totalSearches}} searches

---

Present this strategy clearly and ask user to confirm or adjust.
```

#### rs_prompt_adjust_strategy(## Strategy Adjustment)

```
## Strategy Adjustment

User wants to adjust the proposed strategy.

**Current Strategy**:
{{proposedStrategy}}

**User's Adjustments**:
{{userAdjustments}}

## Your Task

Merge user's feedback with current strategy:

1. **If user wants different angles**: Update researchAgentConfigs
2. **If user wants more/fewer agents**: Adjust numbers
3. **If user wants different modes**: Update modes
4. **If user wants specific focus areas**: Add to deep research

## Output

Update the strategy object and present the revised version.

Loop back to rs_prompt_propose_strategy with updated configuration.
```

#### rs_prompt_create_tasks(## Execute Research Strategy)

```
## Execute Research Strategy

User has confirmed the strategy. Now spawn all sub-agents in parallel.

**Confirmed Strategy**:
{{proposedStrategy}}

## Your Task

Create Task tool calls for ALL agents in a SINGLE message (parallel execution).

### For each Research Agent (rs-agent-research):

Use Task tool with:
- subagent_type: "rs-agent-research"
- model: "haiku" (fast, efficient for quick research)
- prompt: Include ALL context:
  ```
  Research Topic: {{coreTopic}}
  Assigned Angle: [specific angle]
  Search Mode: [mode]
  Required Queries: {{queriesPerAgent}}
  ```

### For each Deep Research Agent (rs-agent-deep-research):

Use Task tool with:
- subagent_type: "rs-agent-deep-research"
- model: "sonnet" (more capable for deep research)
- prompt: Include ALL context:
  ```
  Research Topic: {{coreTopic}}
  Assigned Focus: [specific focus]
  Search Mode: [mode]
  Required Queries: {{queriesPerAgent}}
  ```

## CRITICAL

- Launch ALL agents in a SINGLE message with multiple Task tool calls
- This enables parallel execution for maximum efficiency
- Each agent operates independently and returns its results

## Output

After all agents complete, collect all results for merging.
```

#### rs_prompt_merge_results(## Merge Research Results)

```
## Merge Research Results

All sub-agents have completed. Now merge their results.

**Research Results from Quick Agents**:
{{researchResults}}

**Deep Research Results**:
{{deepResearchResults}}

## Your Task

Merge all results by:

### 1. Group Findings by Theme
Identify common themes across all agent outputs

### 2. Identify Consensus
What do multiple agents agree on?

### 3. Identify Conflicts
Where do agents disagree?

### 4. Compile Edge Cases & Limitations
From deep research agents

### 5. Compile Counterarguments
From deep research agents

### 6. Compile All Sources
Deduplicate and organize all sources

## Output

Merged research data ready for synthesis.
```

#### rs_prompt_synthesize(## Synthesize Final Report)

```
## Synthesize Final Report

Create a comprehensive research report from merged results.

**Topic**: {{coreTopic}}
**Merged Data**:
- Themes: {{mergedThemes}}
- Consensus: {{consensusPoints}}
- Conflicts: {{conflictPoints}}
- Edge Cases: {{edgeCases}}
- Counterarguments: {{counterarguments}}
- Sources: {{allSources}}

## Report Structure

# Research Report: {{coreTopic}}

## Executive Summary
[2-3 paragraph high-level summary]

## Research Methodology
- **Effort Level**: {{effortLevel}}
- **Total Searches**: {{totalSearches}}
- **Research Agents**: {{numResearchAgents}}
- **Deep Research Agents**: {{numDeepAgents}}

## Key Findings
[Findings with confidence levels]

## Consensus Points
[What sources agree on]

## Contested Areas
[Where there is disagreement]

## Edge Cases & Limitations
[Exceptions and limits]

## Counterarguments & Alternative Perspectives
[Critical views]

## Recommendations
[Actionable advice]

## Confidence Assessment
- High Confidence: [...]
- Medium Confidence: [...]
- Needs More Research: [...]

## All Sources
[Complete list]
```

#### rs_prompt_present_report(## Present Research Report)

```
## Present Research Report

Present the final report to the user.

**Topic**: {{coreTopic}}
**Report**: {{finalReport}}

---

Display the full report to the user.

User có thể:
1. **Đã đủ** - Kết quả research đầy đủ
2. **Cần thêm** - Muốn research thêm về một khía cạnh cụ thể
```

#### rs_prompt_collect_followup(## Followup Research Request)

```
## Followup Research Request

User wants additional research.

**Current Topic**: {{coreTopic}}
**Current Report**: {{finalReport}}

**User's Followup Request**:
{{userFollowup}}

## Your Task

1. Understand what additional research the user wants
2. Determine if this is:
   - A deeper dive into existing topic
   - A related but new topic
   - A specific question from the report

3. Prepare for another guide search cycle with:
   - Updated or refined topic
   - Preserved context from previous research
   - Specific focus areas from user request

## Output

Update the research topic and configuration, then loop back to guide search.
```

#### rs_prompt_final_output(## Research Complete)

```
## Research Complete

**Topic**: {{coreTopic}}
**Final Report**: {{finalReport}}

---

# ✅ Research Complete

## Summary
- **Topic**: {{coreTopic}}
- **Effort Level**: {{effortLevel}}
- **Total Searches Performed**: {{totalSearches}}
- **Sources Analyzed**: {{sourceCount}}
- **Confidence Level**: {{overallConfidence}}

## What Was Covered
{{coverageSummary}}

## Key Takeaways
1. {{keyTakeaway1}}
2. {{keyTakeaway2}}
3. {{keyTakeaway3}}

---

Research workflow complete. User can use these findings for decision-making or further exploration.
```

### AskUserQuestion Node Details

Ask the user and proceed based on their choice.

#### rs_question_initial_config(Chọn cấu hình research)

**Selection mode:** Single Select (branches based on the selected option)

**Options:**
- **Low - Nhanh (3 queries/agent)**: Overview nhanh, phù hợp câu hỏi đơn giản
- **Medium - Cân bằng (5 queries/agent)**: Research cân bằng giữa tốc độ và độ sâu
- **High - Toàn diện (10 queries/agent)**: Research sâu, nhiều góc nhìn

#### rs_question_confirm_strategy(Confirm hoặc điều chỉnh strategy?)

**Selection mode:** Single Select (branches based on the selected option)

**Options:**
- **Confirm, thực hiện**: Strategy này ổn, bắt đầu research
- **Điều chỉnh**: Muốn thay đổi số agents hoặc focus areas

#### rs_question_satisfied(Báo cáo có đủ thông tin không?)

**Selection mode:** Single Select (branches based on the selected option)

**Options:**
- **Đã đủ, hoàn tất**: Research đã đủ thông tin
- **Cần thêm thông tin**: Muốn research thêm về một khía cạnh cụ thể
