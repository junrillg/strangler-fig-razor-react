---
name: Requisition desk
description: A clear, ruled workspace for requests and recorded decisions.
colors:
  ground: "#f3f5f8"
  ink: "#172534"
  muted: "#516276"
  accent: "#155f73"
  accent-hover: "#104b5d"
  plane: "#ffffff"
  rule: "#d3dce4"
  control-line: "#748594"
  selected: "#e6eff3"
  danger: "#a32832"
  success: "#23634b"
  rail-text: "#d8e2eb"
  focus: "#236f87"
  queue-ground: "#f8fafb"
  action-ground: "#f3f7f9"
typography:
  headline:
    fontFamily: "Source Sans 3, sans-serif"
    fontSize: "1.875rem"
    fontWeight: 650
    lineHeight: 1.2
    letterSpacing: "-.025em"
  review-title:
    fontFamily: "Source Sans 3, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 650
    lineHeight: 1.25
  title:
    fontFamily: "Source Sans 3, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 650
    lineHeight: 1.2
  body:
    fontFamily: "Source Sans 3, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "Source Sans 3, sans-serif"
    fontSize: "1rem"
    fontWeight: 600
    lineHeight: 1.5
  metadata:
    fontFamily: "Source Sans 3, sans-serif"
    fontSize: ".875rem"
    fontWeight: 400
    lineHeight: 1.5
rounded:
  field: "3px"
  button: "4px"
spacing:
  quarter: ".25rem"
  half: ".5rem"
  three-quarter: ".75rem"
  one: "1rem"
  one-quarter: "1.25rem"
  one-half: "1.5rem"
  two: "2rem"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.plane}"
    rounded: "{rounded.button}"
    padding: ".625rem 1.1rem"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
    textColor: "{colors.plane}"
  button-secondary:
    backgroundColor: "{colors.plane}"
    textColor: "{colors.ink}"
    rounded: "{rounded.button}"
    padding: ".625rem 1.1rem"
  field:
    backgroundColor: "{colors.plane}"
    textColor: "{colors.ink}"
    rounded: "{rounded.field}"
    padding: ".65rem .75rem"
  reading-plane:
    backgroundColor: "{colors.plane}"
    textColor: "{colors.ink}"
  queue-selected:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.ink}"
    padding: "1.1rem 1.25rem"
  status:
    textColor: "{colors.muted}"
  status-submitted:
    textColor: "{colors.accent}"
  status-approved:
    textColor: "{colors.success}"
  status-rejected:
    textColor: "{colors.danger}"
---

# Design System: Requisition desk

## Overview

**Creative North Star: "Review sheet"**

A calm, compact workspace built from slate ground, ink navigation and white reading planes. Crisp dividing rules establish the structure; petrol marks actions and selection. The request, its available action and its recorded history form one continuous reading surface.

The interface uses real text and native controls. Its character comes from careful alignment, restrained color and the readable Source Sans 3 family. Long content wraps, forms retain clear labels, and narrow screens preserve the reading order.

**Key Characteristics:**

- Flat, ruled surfaces with small control corners.
- One action accent with explicit text for status and selection.
- A compact type hierarchy and generous room inside the main reading surface.
- Native navigation, visible focus and stacked mobile content.

The visual source is [the shared stylesheet](src/Gateway/wwwroot/assets/site.css) and [the Razor pages](src/Monolith/Pages). Font files are self-hosted; their immutable source, checksum and license are recorded in [the font provenance](src/Gateway/wwwroot/assets/SourceSans3-PROVENANCE.md).

## Colors

Cool neutral surfaces support dark text and a restrained petrol accent. Frontmatter records the shared values.

### Primary

- **Petrol accent:** primary actions, links, submitted status, selected-row borders and native radio controls. The deeper accent is the hover treatment.
- **Focus petrol:** keyboard outlines on the light reading surface.

### Neutral

- **Slate ground:** the page canvas and subtle record-row hover.
- **Ink:** primary text and the navigation rail.
- **Muted slate:** supporting copy, timestamps, metadata and draft status.
- **White plane:** forms, reading surfaces and light text on filled actions.
- **Divider slate:** structural rules separating sections and records.
- **Control slate:** input outlines and secondary-button boundaries, intentionally stronger than divider rules.
- **Selected mist:** the chosen queue row.
- **Rail mist:** text on dark navigation.
- **Queue ground / action ground:** quiet tonal separation for the request list, state messages and action sections.

Success green and danger red identify approved and rejected states. Error notices additionally use a pale red ground and visible border; recovered text uses a pale warm ground. These local treatments do not become alternate action palettes.

**The Boundary Rule.** Structural dividers and interactive outlines have different roles; use the stronger control boundary for fields and secondary buttons.

**The Written State Rule.** State color accompanies a written status. Selection also has a border and a visible “Selected” label.

## Typography

**Display and Body Font:** Source Sans 3, with a sans-serif fallback. The self-hosted upright variable face supports weights 200–900, uses swap loading, and disables synthetic styles.

The hierarchy is compact and task-oriented rather than promotional. Headings use weight 650, labels and record titles use 600, and body copy uses regular weight. There is no separate display or monospace face.

### Hierarchy

- **Headline:** page titles; reduces to 1.625rem at the narrow breakpoint.
- **Review title:** the selected request heading; reduces to 1.375rem on narrow screens and stays within 48ch.
- **Title:** recurring plane headings, queue headings and subsection headings. Generic second-level headings use 1.375rem with a 1.2 line-height and -.015em tracking.
- **Body:** readable text with a paragraph measure of 70ch; multiline request and note text preserves line breaks.
- **Label:** explicit form labels; buttons use the same weight with a tighter 1.25 line-height.
- **Metadata:** timestamps and record details. Timestamps and revision metadata use tabular numerals. Status uses this size at weight 600; selection labels step down to .8125rem.

**The Single Family Rule.** Establish hierarchy through size, weight and spacing within Source Sans 3.

## Layout

The desktop shell is a two-column grid: a 12.5rem navigation rail and a flexible main region. Main content has 2.5rem top/side padding and 3rem bottom padding, with no fixed maximum width. Page headers wrap and separate from content by 1.75rem.

The review sheet pairs a 17.5rem queue with a flexible request pane and has a 39rem minimum height. The request pane uses horizontal rules to divide its header, context or form, action area and history. Main pane sections generally have 2rem horizontal padding. Other reading planes use 1.5rem internal padding. Overview and notes use unequal two-column grids with a 1.5rem gap.

The repeated spacing vocabulary is quarter-rem based, with larger section gaps. Small optical adjustments in individual controls are local values, not additional scale steps.

| Maximum viewport width | Layout behavior |
| --- | --- |
| 1150px | Main side padding becomes 1.75rem; the review queue becomes 15rem; pane side padding becomes 1.5rem. Overview and notes become one column. History timestamps move below their event. |
| 850px | Rail becomes 10.5rem. Queue moves above the request; its entries use two columns with a 15rem scroll limit. Sheet minimum height is removed. |
| 620px | Navigation becomes a top band with wrapping links and an inline account row. Main side padding becomes 1rem. Queue becomes one column with a 13.5rem scroll limit. Pane sections use 1.35rem vertical and 1.25rem horizontal padding. |

Long titles and user text wrap anywhere when needed. Flex rows wrap instead of imposing horizontal scrolling. The sign-in plane is capped at 26rem and uses the same field, action and rule vocabulary.

## Elevation & Depth

There are no box shadows. Depth comes from the dark navigation ground, the slate canvas, white reading planes and quiet tonal section backgrounds. Thin rules provide edges and grouping without lifting surfaces.

**The Ruled Plane Rule.** Separate adjoining content with rules and tonal grounds; keep the reading plane flat.

## Shapes

Containers and review sections have square corners and thin borders. Small softening is reserved for controls: fields and navigation items use the field radius, and buttons use the button radius. Status remains plain text rather than a pill. Native radio circles retain their familiar shape.

## Components

### Buttons

Compact, clearly bounded actions. Filled buttons use petrol and white; secondary buttons use a white plane, ink text and the stronger control outline. Both have a minimum height of 2.75rem and use the frontmatter padding and button radius.

Primary hover deepens the accent and active press darkens it further. Secondary hover lightly tones the ground and strengthens the border; active press adds a deeper tint. Background transitions take 160ms with cubic-bezier(.16, 1, .3, 1). Reduced-motion preference removes transitions. Disabled controls use .58 opacity and a not-allowed cursor. Sign-out is an underlined text button on the rail.

### Cards / Containers

Reading planes are square white containers with a divider border. Their headings and list rows are separated by rules. Empty states are explanatory text with an optional link, using the same surface rather than an illustration or floating card.

### Inputs / Fields

Explicit labels sit above full-width white fields. Inputs have a 2.75rem minimum height; textareas have a 6rem minimum height and resize vertically. Muted help sits directly below the field.

Hover strengthens the boundary. Keyboard focus changes the border to petrol and uses a 2px outline with a 1px offset. Validation errors use the danger boundary; error notices show readable explanatory text in an alert region. Recovered input stays readable in labeled, read-only fields.

Native radios sit beside their labels with the accent color. Decision choices wrap on narrow layouts.

### Navigation

The desktop rail is dark ink with light text. Link hover adds a lighter ink ground; the current page adds a stronger tonal ground and weight 650. The current route is expressed with `aria-current`. Account information rests at the bottom on desktop and moves into the top band on mobile.

A skip link appears on keyboard focus. Global keyboard focus uses a 3px outline and 4px offset; the rail switches to a pale outline for contrast against ink.

### Request Queue and Review Sheet

Queue entries use a title, written status and optional selection label. Hover lightly tones the row. The current request has a mist background and a petrol border, with no movement or animated reveal. Links navigate to the request heading using its native fragment target; that heading accepts focus.

The request pane keeps its heading, labeled context or editable fields, permitted action and history in document order. Submitted, read-only and empty states remain text sections within this plane.

### Recorded History

Ruled rows pair the event and actor with a timestamp. Reasons occupy the full row below. At the first responsive breakpoint, time moves below the event. Timestamps use tabular numerals and remain secondary to the event text.

## Do's and Don'ts

### Do:

- **Do** use the stronger control boundary for interactive fields and secondary actions.
- **Do** keep written status and selection labels alongside color.
- **Do** retain native controls, explicit labels, visible keyboard focus and wrapping content.
- **Do** keep request context, available action and history in one reading sequence.

### Don't:

- **Don't** replace the flat, ruled reading surfaces with lifted or shadowed cards.
- **Don't** convert plain status labels into decorative pills.
- **Don't** shrink the desktop grid into mobile; stack the queue and request and move navigation to the top band.
- **Don't** introduce a second type family or decorative imagery into this text-led workspace.
