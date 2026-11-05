# Administrative profile validation order

An administrative profile patch can contain a valid display name and an invalid
or duplicate email. Previously the service assigned the display name to the
repository-owned User before rejecting the email. The production repository can
return a tracked User; returning failure did not restore that in-memory value.

The service now validates both supplied profile fields before assigning the
display name. Existing authorization, validation order and error messages,
email normalization, status handling, session calls, audit and successful saves
are preserved. The valid display-name assignment occurs after email validation
and assignment; both properties have ordinary setters and no observable
intermediate callback. No modifying dependency runs between those assignments.

The two regression cases use the existing synthetic administrative fixture.
They require the exact invalid/duplicate-email error, unchanged name/email/role/
status and no audit, authorization-change, session-call or save effect. Their
original failure is an Application object-mutation reproduction, not proof that
a normal failing HTTP request later saves the same database context. This fix
does not establish complete transaction atomicity or deployed acceptance.
