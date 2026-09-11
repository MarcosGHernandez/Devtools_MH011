---
name: responsive-layout-engine
version: 1.0.0
description: Implements modern responsive layout strategies, fluid CSS grids, mobile-first breakpoints, container queries, and adaptive micro-interactions.
author: DevTools Core Team
tags: [frontend, responsive, css, grid, flexbox, ui]
parameters:
  layoutType:
    type: string
    description: Desired layout archetype (dashboard, split-pane, content-feed, master-detail).
    required: false
---

# Responsive Layout Engine & Fluid Architecture

## Breakpoint Strategy (Mobile-First)
Standardized CSS viewport breakpoints:
- `sm`: `640px` (Compact mobile / small tablet)
- `md`: `768px` (Tablets / split-screen portrait)
- `lg`: `1024px` (Laptops / landscape tablets)
- `xl`: `1280px` (Standard desktop workstations)
- `2xl`: `1536px` (Ultra-wide displays)

## Fluid Grid Systems
Avoid rigid column pixel counts. Use CSS Grid with `repeat(auto-fit, minmax(...))` for automatically adapting cards and statistics:

```css
/* Dynamic dashboard cards adapting from 1 column on mobile to 4 on widescreen */
.grid-fluid-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 240px), 1fr));
  gap: var(--space-4);
}

/* Split-pane Master-Detail / Chat & Preview layout */
.layout-split-pane {
  display: grid;
  grid-template-columns: 1fr;
  gap: var(--space-6);
}

@media (min-width: 1024px) {
  .layout-split-pane {
    grid-template-columns: 420px 1fr;
    align-items: start;
  }
}
```

## Modern Container Queries (`@container`)
For components that must adapt to their container width rather than the browser window:

```css
.card-container {
  container-type: inline-size;
}

@container (min-width: 400px) {
  .card-body {
    display: flex;
    flex-direction: row;
    justify-content: space-between;
  }
}
```

## Fluid Typography with `clamp()`
Eliminate jarring font jumps across viewport resizes:
```css
h1 {
  font-size: clamp(1.25rem, 1rem + 1vw, 2rem);
  letter-spacing: -0.02em;
}
```
