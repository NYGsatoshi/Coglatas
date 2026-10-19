# Pilot image preview controls

The ready, grant-backed image renderer in the existing Files inspector provides
Fit, 100%, Rotate, Zoom out, and Zoom in. This bounded presentation implements
the image controls from retained Penpot FHV09 board
`9a59a7a6-1369-80f7-8008-cf261fd20680`, file
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`, revision 441. It uses the existing
Approved Dark semantic tokens and Lucide icons. It does not implement the
standalone reference page, transfer queue, or a new safety capability.

Fit preserves aspect ratio and can scale below 25%; it does not enlarge a
smaller image. 100% renders natural image pixels. Zoom starts from the current
scale, is bounded between one quarter of the current fit scale and 400%, and
does not change the file. Rotation advances by 90 degrees. The native,
keyboard-focusable scroll region reserves the rotated image's layout bounds,
so transforms do not make its top or left edges unreachable. Controls wrap
within the inspector at narrow widths.

Controls appear only after the existing preview flow authorizes a matching
File Blob, checks its MIME family, and the browser decodes valid dimensions.
New source URLs clear dimensions, fit mode, rotation, and scroll position;
stale load events cannot restore the old image dimensions. Closing the
inspector, changing File or Workspace, replacing an authoritative inventory or
search snapshot, and clearing the session retain the existing parent cleanup
and object-URL revocation. No new storage, API, permission, flag, upload,
download-grant, or scanning behavior is introduced.

Focused component tests cover fit, actual pixels, wide/portrait rotated bounds,
bounded zoom, resize, replacement/stale loads, decode failure, and authorized
mount/session cleanup. `tests/ui/file-image-viewport.spec.ts` uses deterministic,
decodable synthetic wide and portrait PNGs through mocked cookie/CSRF grant and
Blob responses. It checks controls, keyboard scrolling/focus, all rotated
edges, five viewport widths, no page overflow, replacement, denied grant,
authoritative search denial, Workspace boundary, terminal 401 cleanup, and URL
revocation. The normal static suite discovers this spec.

Browser mocks establish frontend behavior only. They do not qualify server
authorization, a deployed tenant's `FileSharing:false` denial, scanning, public
intake, production deployment, or full FHV09 visual parity. The pilot upload
affordance remains disabled. No screenshot goldens are changed.
