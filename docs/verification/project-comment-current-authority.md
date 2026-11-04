# Legacy Project comment mutation authority

Legacy PATCH and DELETE on /api/comments/{id} now recheck the active,
undeleted account, stored comment identity, undeleted parent, and current
authorization before mutating the tracked comment. A current manager can
still correct eligible historical comments, including in archived Projects.
An ordinary author must still hold the existing contribution grant on the
parent. The canonical TaskComment lookup remains first and a canonical
denial does not fall back to a legacy comment. Legacy GET and POST and
the existing HTTP error mapping remain unchanged.

The repository reads through the existing scoped context without tracking,
restores the prior mode on success, exception or cancellation, and leaves
caller changes and transaction ownership intact. These are current reads,
with no atomic revocation guarantee through commit.

The unpatched Main reproduction failed eight author edit/delete cases after
committed Workspace or Project membership loss. Ten management controls
passed. Tests after repair cover retained tracked authority, read-only and
viewer demotion, account suspension, archived Workspace, changed comment
identity, unsupported or deleted parents, preserved GET/POST behavior,
unchanged manager grants, audit effects, and cancellation. PostgreSQL
controls also exercise caller rollback and persisted timestamp precision.
These tests do not qualify deployed roles, RLS, or realtime transport.
