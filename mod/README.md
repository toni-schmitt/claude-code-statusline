# Ember Subagent Mod

A [Claude Code](https://code.claude.com) mod that renders subagent rows in the Ember grammar — ember-gradient name, model, reasoning effort, half-cell severity context bar, token count, and description — via Claude Code's `AgentRow` `ui.render` hook.

This replaces the deprecated `subagentStatusLine` command setting (`ember-subagent` binary) on Claude Code v2.1.326+.

## Installing

```sh
claude plugin install ember-subagent --marketplace toni-schmitt/claude-code-statusline
```

Or inside a Claude Code session:

```text
/plugin marketplace add toni-schmitt/claude-code-statusline
/plugin install ember-subagent@ember
```

## Configuration

The default icon set (`nerd`) requires a Nerd Font. To switch to plain Unicode or ASCII:

```sh
claude plugin install ember-subagent --marketplace toni-schmitt/claude-code-statusline --config icons=unicode
```

Or reconfigure an existing install with `/plugin configure ember-subagent@ember`.
