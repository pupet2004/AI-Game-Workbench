# Project Library Axis Browsing

## Goal

Turn Project Library from a flat document-like list into three explicit browsing axes: category evolution, daily activity, and current project state.

## Scope

The first release reuses the existing Library Object, Timeline Node, Daily Summary, Summary Delta, and Accepted State records. It does not add database tables or change authority semantics.

## Behavior

- Category browsing selects a category, then an object, then shows that object's vertical timeline.
- Time browsing selects a date, then shows the Daily Summary, decisions/changes for that date, and linked Library nodes.
- Project browsing shows current Accepted State, project overview, and the existing project metadata.
- Overview remains the entry point for current state, recent activity, and pending Library proposals.
- Clicking the selected category, object, or date again clears that selection.
- Leader-generated taxonomy and Library mutations continue through the existing proposal and user-confirmation flow.

## Invariants

- Summary, Decision, Accepted State, and Library Node remain distinct projections.
- Browsing never writes continuity or authority state.
- No horizontal scrolling is introduced for the Library surface.

## Acceptance

- Clicking a category does not render an undifferentiated document; it exposes its objects and latest update.
- Clicking an object exposes a chronological vertical timeline.
- Clicking a date exposes that day's summary and summary entries before linked Library nodes.
- Repeated selection collapses the corresponding detail view.
