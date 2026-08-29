# Project Library Axis Browsing

## Goal

Turn Project Library from a flat document-like list into four explicit entry points: current overview, category evolution, chronological activity, and project metadata.

## Scope

The first release reuses the existing Library Object, Timeline Node, Daily Summary, Summary Delta, and Accepted State records. It does not add database tables or change authority semantics.

## Behavior

- Overview shows the current accepted project state and important present-tense statements.
- Category browsing selects a semantic category, then an object, then shows that object's vertical evolution timeline.
- Time browsing drills down year → month → day, then shows dense timestamped events for that day.
- Project browsing shows project metadata; its final information architecture remains intentionally open.
- Timeline and summary source references are available behind a compact Source expander.
- Clicking the selected category, object, year, month, or date again clears that selection.
- Leader-generated taxonomy and Library mutations continue through the existing proposal and user-confirmation flow.

## Invariants

- Summary, Decision, Accepted State, and Library Node remain distinct projections.
- Browsing never writes continuity or authority state.
- No horizontal scrolling is introduced for the Library surface.

## Acceptance

- Clicking a category does not render an undifferentiated document; it exposes its objects and latest update.
- Clicking an object exposes a chronological vertical timeline.
- Clicking a year exposes months; clicking a month exposes days; clicking a day exposes timestamped events with category and object labels.
- Repeated selection collapses the corresponding detail view.
