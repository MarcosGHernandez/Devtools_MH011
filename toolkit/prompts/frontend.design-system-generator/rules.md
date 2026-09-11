# Frontend Design System Rules

## Rule 1: Zero Arbitrary Values
- All components must map to designated design tokens (spacing, color, border-radius, font-size).
- No inline hardcoded pixel values (e.g. `margin: 17px;`). Use standardized 4px/8px scale tokens.

## Rule 2: High Contrast Accessibility (WCAG 2.2 AA)
- Normal text must have at least 4.5:1 contrast against its immediate background.
- Large headers and bold text must have at least 3.0:1 contrast.
- Interactive controls must have an unmistakable focus ring (`outline: 2px solid var(--color-border-focus)`).

## Rule 3: Responsive Mobile-First
- Designs must function seamlessly down to 320px viewport width without horizontal scrollbars.
- Touch interactive areas must meet the 44px minimum sizing target on touch-enabled devices.

## Rule 4: Classic Minimalist Aesthetic
- Maintain a high signal-to-noise ratio: avoid non-functional decorative clutter, heavy gradients, or gratuitous animations.
- Micro-interactions must be snappy (100ms - 200ms ease-out transitions).
