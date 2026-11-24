# Unified GPO readiness: readable timer review

The existing read-only pilot review now wraps observations and next steps, fills the available grid width, and automatically sizes displayed rows. The detail pane uses a readable UI font and vertical scrolling. Timer comparisons show a localized title and relation (before/equal/after/never), the AD or campaign reference, and the exact unmodified GPO name, GUID and source file on a separate line.

Timer observations remain canonical English in the underlying finding. The UI renders structured data for English, Russian and Hebrew, including unresolved timer states. Changing the interface language re-renders existing row tags and the selected detail without requesting a new AD scan or changing any evidence, campaign, credentials or policy.

Native offline presentation fixtures cover raw identity and numeric preservation, unresolved and zero timer semantics, and EN/RU/HE rendering. The existing GUI smoke additionally checks language switching, selected-row details, wrapping and LTR technical fields at 100/150/200 percent simulated UI scale. This is not a physical desktop DPI signoff, and other dynamic diagnostic messages remain partly English.
