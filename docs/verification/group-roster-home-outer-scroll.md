# Group roster Home and End across nested scrollports

Main `1dcb3244bf2d8e2c6a620bda800eaad6975431d5` first-attempt CI
`42251339047/1`, job `128008837766`, retains the UI07 Mobile failure at
`HOME_FIRST_NAME_VISIBILITY`. Its internal Home position is zero, but the first
name and status remain below the enclosing viewport. Desktop and the separate
eight core real-backend cases passed. The finite lifecycle shows no remount
after initial presentation in that sequence; historical initiating causes remain
UNKNOWN. This does not attribute that failure to the Project overview change.

The boundary handler previously changed only the inner list's scroll position.
A native Chromium control at 320x420 additionally checks the first complete row
immediately after Home and again after returning focus to the summary. Before
the product correction, both configured Chromium profiles fail that immediate
visibility assertion with ratio zero. Earlier English/default-locale/mock-shape
controls passed after Shift+Tab and are retained as narrower evidence; they do
not reproduce the complete hosted failure. The initial runner preparation failed
dependency resolution before browser execution and is retained separately.

Independent review exposed two additional gaps in the initial centering repair:
a row taller than the list can move inner Home above zero, and an outer-only
overflow is skipped after a short roster remount. Separate controls retain both
failures. The correction scrolls only enclosing containers, showing summary and
first row together when they fit, or aligning the beginning of an oversized row.
The list remains at Home zero. A short fresh roster still handles ancestor
overflow. The same operation uses only the newly authorized first row when
restoring a pending Home across an authorized remount. It does not move
focus, retain an old DOM node, accept modified/child/IME keys, or
change membership/authorization state. Existing native boundary, original
ReadOnly owner, retry, deadline, lifecycle and authority assertions remain.

Further review identified integer `clientHeight` accepting a fractionally clipped
edge and a document root using document instead of viewport coordinates. Separate
geometry controls retain those counterexamples. The helper uses fractional
rectangle bounds with border/scrollbar accounting and a one-pixel inset; the
document root uses viewport-origin bounds. Native browser controls cover three
fractional ancestor heights, full first-name/status and summary visibility,
Shift+Tab, and inner Home zero. These do not establish a historical initiating
cause or universal arbitrary-transform/zoom acceptance.

An independent inline review additionally identified End remaining clipped after
Home repositioned the enclosing scrollport. A separate native Home/End reversal
control first fails both profiles before Tab. Both boundaries now expose their
corresponding row through ancestors while keeping inner zero/maximum and focus.
An oversized final row exposes its end. A fresh short roster also restores End
through an overflowing ancestor. The original End assertions remain unchanged.
Additional native Chromium controls exercise both cases: the final status of an
oversized final row remains visible at inner End maximum; a three-member roster
fits its list but overflows a real enclosing scrollport, then restores End after
a routine authorized Realtime catch-up. A first version used manual Refresh,
which intentionally discards focus intent; its two focus failures are retained
as a mismatched harness path. The corrected routine-catch-up control passes.

An independent platform probe with artificial CSS heights 100.75px and 100.9px
rounds both clientHeight and scrollHeight to 101. Native scrollTop remains zero
despite subpixel clipping. This explicit native quantization limit does not
establish a product regression or universal clipping acceptance. The functional
roster controls exercise actual overflowing inner and enclosing scrollports.

Main90 independently passed the protected roster before this unmerged repair.
That successful run is not proof of the historical initiating cause, and the
retained Main1dcb failure remains a separate observation.

This isolated mock-HTTP browser regression and Angular unit execution are
separate from exact-head protected real-backend roster execution, required CI,
official PR Gate, independent review and resulting-Main qualification. They do
not close #1346, historical MSG/Thread failures, real Pilot/physical/HTTPS
acceptance, or authorize Release GO.
