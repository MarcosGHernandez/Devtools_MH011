---
name: frontend-design-system
version: 1.0.0
description: Guides the creation and audit of minimalist frontend design systems, design tokens, color palettes, fluid typography, and dark/light CSS variables.
author: DevTools Core Team
tags: [frontend, design-tokens, ui, css, minimalist]
parameters:
  themeName:
    type: string
    description: The name of the design system or theme.
    required: false
  accentColor:
    type: string
    description: Primary accent hue (e.g. slate-blue, emerald, monochrome).
    required: false
---

# Frontend Design System & Token Architecture

## Philosophy: Classic Minimalist
The classic minimalist aesthetic prioritizes clarity, content hierarchy, and restraint:
1. **Monochrome Base**: Ground surfaces and text in curated slate/neutral scales (`#0d0f12`, `#14171d`, `#242b35`, `#f0f3f6`).
2. **Restrained Accents**: Reserve vibrant colors strictly for semantic signals (success, warning, error) and high-priority call-to-actions.
3. **Subtle Depth**: Avoid heavy drop-shadows or gradients; rely on 1px borders, subtle surface elevations, and restrained opacity layers.
4. **Typography-First**: Use high-legibility sans-serif typefaces (e.g., `Inter`, `Geist`, `Segoe UI`) paired with crisp monospace typefaces (e.g., `JetBrains Mono`) for data and code.

## Core Design Tokens (CSS Custom Properties)

```css
:root {
  /* Surface & Backgrounds */
  --color-bg: #0d0f12;
  --color-surface: #14171d;
  --color-surface-hover: #1c212a;
  --color-surface-active: #232934;

  /* Borders & Dividers */
  --color-border: #242b35;
  --color-border-subtle: #1a2028;
  --color-border-focus: #4b586c;

  /* Typography Colors */
  --color-text-primary: #f0f3f6;
  --color-text-secondary: #8c97a8;
  --color-text-muted: #576171;

  /* Semantic Accents */
  --color-accent: #3b82f6;
  --color-accent-subtle: rgba(59, 130, 246, 0.12);
  --color-success: #10b981;
  --color-warning: #f59e0b;
  --color-danger: #ef4444;

  /* Typography Scale */
  --font-sans: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  --font-mono: 'JetBrains Mono', 'Fira Code', monospace;
  --text-xs: 0.75rem;    /* 12px */
  --text-sm: 0.8125rem;  /* 13px */
  --text-base: 0.875rem; /* 14px */
  --text-md: 1rem;       /* 16px */
  --text-lg: 1.125rem;   /* 18px */
  --text-xl: 1.375rem;   /* 22px */
  --text-2xl: 1.75rem;   /* 28px */

  /* Spacing Scale (8pt grid with 4pt half-steps) */
  --space-1: 4px;
  --space-2: 8px;
  --space-3: 12px;
  --space-4: 16px;
  --space-5: 20px;
  --space-6: 24px;
  --space-8: 32px;
  --space-12: 48px;

  /* Radii */
  --radius-sm: 4px;
  --radius-md: 6px;
  --radius-lg: 10px;
  --radius-full: 9999px;

  /* Transitions */
  --transition-fast: 120ms ease;
  --transition-normal: 200ms ease;
}
```

## Token Audit Checklist
When reviewing or generating frontend components, enforce:
- [ ] No hardcoded hex or rgb values outside the token stylesheet.
- [ ] Touch targets are at least 36px high for desktop and 44px for touch screens.
- [ ] Text contrast ratios satisfy WCAG 2.2 AA (minimum 4.5:1 for normal text, 3:1 for large text).
- [ ] Focus states are distinct and visible (`outline: 2px solid var(--color-border-focus)`).
