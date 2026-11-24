# Fleet status layout at large UI scales

Client Health totals, AD/SMB coverage and current status share a bounded vertical scroll region with automatic wrapping, rather than separate fixed-height rows. Agent Management uses the same region for AD, SMB and RPC status. Live status changes retain wrapping and the existing status colors. Client Health column headers and visible data rows wrap and size to their contents; the selected-agent details remain word-wrapped and scrollable.

No actions, remote reads, scope checks, filters, client report fields or Repair confirmation requirements are removed or changed. The layout keeps the inventory grid in its existing flexible region. Technical evidence remains LTR.

Existing offline UI tests now verify both status regions, header/row wrapping, and a long synthetic live-status refresh. Real monitor DPI/RTL acceptance is still required.
