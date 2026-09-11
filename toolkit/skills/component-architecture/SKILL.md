---
name: component-architecture
version: 1.0.0
description: Defines best practices for modern UI component composition, atomic design hierarchy, container/presentational split, props contracts, and state boundaries.
author: DevTools Core Team
tags: [frontend, architecture, components, ui, react, vue, blazor]
parameters:
  framework:
    type: string
    description: Target framework (React, Vue, Blazor, Svelte, Web Components).
    required: false
---

# UI Component Architecture Specification

## Hierarchy: Atomic Composition
To maintain maintainable, highly scalable web frontends, components are classified into four strict layers:

### 1. Atoms (Base Primitives)
- Single-responsibility UI building blocks without business logic.
- Examples: `Button`, `Input`, `Badge`, `Spinner`, `Checkbox`, `Avatar`.
- Rules: Must accept standard HTML attributes and forward refs. Purely presentational.

### 2. Molecules (Simple Composite Controls)
- Combinations of 2-3 atoms operating as a functional unit.
- Examples: `SearchBar` (Input + Button), `FormField` (Label + Input + ErrorText), `StatCard` (Icon + Value + Label).
- Rules: May manage internal transient state (e.g. input focus), but not application domain state.

### 3. Organisms (Contextual Sections)
- Complex sections combining atoms, molecules, and layout structures.
- Examples: `ProjectNavbar`, `BlueprintViewer`, `AdrListPanel`, `ReviewResultTable`.
- Rules: Communicate via typed callbacks (`onSelect`, `onSubmit`) or read from explicit context stores.

### 4. Views / Templates (Page Assemblies)
- Full-page layout structures wiring route data to organisms.
- Handles data fetching, error boundaries, and top-level page state.

## Container vs. Presentational Split
```
📁 src/components/
├── 📁 primitives/          <-- Pure Presentational Atoms & Molecules
│   ├── Button.tsx
│   ├── Badge.tsx
│   └── StatCard.tsx
├── 📁 organisms/           <-- Domain Compositions
│   ├── BlueprintPreview.tsx
│   └── InteractiveChat.tsx
└── 📁 containers/          <-- Data Fetching, State Orchestration & Side Effects
    └── ProjectPlannerContainer.tsx
```

## State Management Guidelines
1. **Local Transient State**: Use component-local state (`useState`) for toggles, accordion collapses, and temporary input text.
2. **Shared Application State**: Use a lightweight store (Zustand, Pinia, Blazor CascadingValues) only when 3+ distant components require read/write access.
3. **Server State**: Always separate server caching from UI state (use TanStack Query, SWR, or RTK Query) to handle deduplication, background polling, and loading states.
