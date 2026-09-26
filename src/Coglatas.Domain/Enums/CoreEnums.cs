using System.Text.Json.Serialization;

namespace Coglatas.Domain.Enums;

public enum UserStatus
{
    Active = 0,
    Suspended = 1,
    Archived = 2
}

public enum SystemRole
{
    NormalUser = 0,
    User = NormalUser,
    Staff = 1,
    Teacher = 2,
    Admin = 3,
    PlatformOperator = 4,
    PlatformAdmin = 5,
    SystemAdmin = PlatformAdmin
}

public enum AppMode
{
    SaaS = 0,
    OnPremSingleTenant = 1,
    OnPremMultiTenant = 2
}

public enum TenantResolutionStrategy
{
    Host = 0,
    Subdomain = 1,
    HeaderForDevelopmentOnly = 2,
    Session = 3,
    ConfigDefault = 4
}

public enum TenantStatus
{
    Active = 0,
    Suspended = 1,
    Archived = 2,
    Deleted = 3
}

public enum TenantUserRole
{
    Owner = 0,
    Admin = 1,
    Staff = 2,
    Member = 3,
    Guest = 4
}

public enum TenantUserStatus
{
    Active = 0,
    Suspended = 1,
    Invited = 2,
    Left = 3,
    Archived = 4
}

public enum WorkspaceStatus
{
    Active = 0,
    Archived = 1,
    Deleted = 2
}

public enum WorkspaceRole
{
    Owner = 0,
    Admin = 1,
    Adviser = 2,
    Member = 3,
    ReadOnly = 4
}

public enum MembershipStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2
}

public enum GroupRole
{
    Owner = 0,
    Admin = 1,
    Adviser = 2,
    Member = 3,
    ReadOnly = 4
}

public enum GroupType
{
    Committee = 0,
    Club = 1,
    ProjectGroup = 2,
    Team = 3,
    Temporary = 4,
    Other = 5
}

public enum GroupStatus
{
    Active = 0,
    Archived = 1,
    Deleted = 2
}

public enum ChannelType
{
    Public = 0,
    Private = 1,
    Announcement = 2,
    Confidential = 3
}

public enum ChannelStatus
{
    Active = 0,
    Archived = 1,
    Deleted = 2
}

public enum ChannelRole
{
    Admin = 0,
    Member = 1,
    ReadOnly = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConversationType
{
    DirectMessage = 0,
    ProjectChannel = 1,
    Thread = 2,
    CommitteeChannel = 3,
    AnnouncementThread = 4,
    ExternalSharedChannel = 5,
    LegalHoldConversation = 6,
    Group = 7,
    ProjectLinked = 8,
    System = 9,
    WorkspaceChannel = 10
}

public enum ConversationMemberRole
{
    Admin = 0,
    Member = 1,
    ReadOnly = 2
}

public enum ReadScopeType
{
    Channel = 0,
    Conversation = 1,
    Post = 2,
    Announcement = 3
}

public enum NotificationType
{
    DirectMessage = 0,
    Mention = 1,
    Announcement = 2,
    Invitation = 3,
    TaskAssigned = 4,
    TaskDueSoon = 5,
    TaskStatusChanged = 6,
    ArtifactUploaded = 7,
    FeedbackCreated = 8,
    ProjectUpdated = 9,
    System = 10,
    Event = 11,
    Message = DirectMessage,
    Assignment = TaskAssigned,
    Feedback = FeedbackCreated
}

public enum EventStatus
{
    Draft = 0,
    Published = 1,
    Cancelled = 2,
    Completed = 3,
    Archived = 4
}

public enum AttendanceStatus
{
    Unanswered = 0,
    Attending = 1,
    NotAttending = 2,
    Maybe = 3
}

public enum DataClassification
{
    Public = 0,
    StudentRecordRestricted = 1,
    InternalSchoolOperational = 2,
    UnknownSensitive = 3,
    Internal = 4,
    Private = 5
}

/// <summary>
/// The baseline audience for a Workspace-owned file. Explicit grants can add
/// individual Workspace members or existing Project-scoped external users;
/// they never turn an email address or a browser-held identifier into access.
/// </summary>
public enum FileSharingPolicy
{
    Private = 0,
    Workspace = 1
}

/// <summary>
/// The server-recorded kind of an explicit File access grant. Its current
/// effectiveness is re-evaluated from the corresponding membership boundary
/// on every read and download authorization.
/// </summary>
public enum FileAccessGrantRecipientKind
{
    WorkspaceMember = 0,
    ExternalProjectMember = 1
}

public enum SchoolRole
{
    Student = 0,
    Guardian = 1,
    Teacher = 2,
    HomeroomTeacher = 3,
    GradeTeacher = 4,
    StudentAdmin = 5,
    SchoolAdmin = 6,
    ExternalGuest = 7
}

public enum FormType
{
    Survey = 0,
    Application = 1,
    AttendanceSupplement = 2,
    Preference = 3,
    Checklist = 4,
    Other = 5
}

public enum FormStatus
{
    Draft = 0,
    Open = 1,
    Closed = 2,
    Archived = 3
}

public enum FormQuestionType
{
    ShortText = 0,
    LongText = 1,
    SingleChoice = 2,
    MultipleChoice = 3,
    Boolean = 4,
    Date = 5,
    Number = 6
}

public enum AnnouncementPriority
{
    Normal = 0,
    Important = 1,
    Urgent = 2
}

/// <summary>
/// Durable, server-owned publication state for an Announcement draft. A draft
/// may be saved, scheduled for one resolved UTC instant, and then published
/// exactly once. Publication failures are retained as safe diagnostics on the
/// scheduled draft; they never produce a fake Published state.
/// </summary>
public enum AnnouncementDraftStatus
{
    Draft = 0,
    Scheduled = 1,
    Published = 2
}

public enum SourceType
{
    User = 0,
    Session = 1,
    Invite = 2,
    Workspace = 3,
    Group = 4,
    Channel = 5,
    Post = 6,
    Message = 7,
    Announcement = 8,
    Project = 9,
    TaskItem = 10,
    ActivityLog = 11,
    Artifact = 12,
    ArtifactVersion = 13,
    Attachment = 14,
    Feedback = 15,
    Comment = 16,
    SecurityEvent = 17
}

public enum SecurityEventType
{
    LoginSuccess = 0,
    LoginFailure = 1,
    Logout = 2,
    PasswordChanged = 3,
    AccessDenied = 4,
    RateLimitTriggered = 5,
    SuspiciousFileUpload = 6,
    InviteAccepted = 7,
    InviteRejected = 8,
    AccountSuspended = 9,
    LoginLockout = 10,
    SessionRevoked = 11,
    SessionValidationFailure = 12
}

public enum SecurityEventSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum OutboxEventStatus
{
    Pending = 0,
    Processing = 1,
    Delivered = 2,
    RetryScheduled = 3,
    DeadLetter = 4,
    Cancelled = 5
}

public enum TaskDeadlineDigestJobStatus
{
    Pending = 0,
    Claimed = 1,
    Succeeded = 2,
    Failed = 3
}

public enum TaskDeadlineDigestAttemptTrigger
{
    Automatic = 0,
    OperatorRestart = 1
}

public enum TaskDeadlineDigestAttemptStatus
{
    Pending = 0,
    Claimed = 1,
    Succeeded = 2,
    Failed = 3,
    Expired = 4,
    Deferred = 5
}

public enum ProjectStatus
{
    Planning = 0,
    Active = 1,
    Review = 2,
    Completed = 3,
    Suspended = 4,
    Archived = 5,
    Deleted = 6
}

public enum ProjectRole
{
    Owner = 0,
    Manager = 1,
    Contributor = 2,
    Reviewer = 3,
    Viewer = 4
}

public enum MilestoneStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public enum TaskItemStatus
{
    NotStarted = 0,
    InProgress = 1,
    WaitingReview = 2,
    Blocked = 3,
    Completed = 4,
    Cancelled = 5
}

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

// These values are persisted as strings. Do not add aliases: the API and migration
// compatibility paths must be able to distinguish every persisted value unambiguously.
public enum WorkItemKind
{
    Task = 0,
    Milestone = 1
}

public enum TaskStageCategory
{
    Backlog = 0,
    Todo = 1,
    InProgress = 2,
    Review = 3,
    Done = 4,
    Cancelled = 5
}

public enum ProjectKanbanSwimlane
{
    None = 0,
    PrimaryAssignee = 1,
    TargetGroup = 2,
    Priority = 3,
    ParentTask = 4
}

public enum TaskReviewStatus
{
    None = 0,
    Submitted = 1,
    Accepted = 2,
    Returned = 3
}

public enum TaskAssignmentRole
{
    Owner = 0,
    Assignee = 1,
    Reviewer = 2,
    Support = 3
}

public enum TaskDependencyType
{
    FinishToStart = 0,
    StartToStart = 1,
    FinishToFinish = 2,
    StartToFinish = 3
}

public enum ActivityLogType
{
    Note = 0,
    StatusUpdate = 1,
    Decision = 2,
    Issue = 3
}

public enum CommentTargetType
{
    Project = 0,
    TaskItem = 1,
    Artifact = 2,
    ArtifactVersion = 3,
    ActivityLog = 4,
    Milestone = 5
}

public enum FeedbackTargetType
{
    Project = 0,
    TaskItem = 1,
    Artifact = 2,
    ActivityLog = 3
}

public enum FileScanStatus
{
    Pending = 0,
    Clean = 1,
    Infected = 2,
    Failed = 3,
    Skipped = 4
}

public enum FileObjectStatus
{
    Active = 0,
    Quarantined = 1,
    Archived = 2,
    Deleted = 3
}

public enum ExportJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum TenantExportType
{
    Metadata = 0,
    AuditPackage = 1
}

public enum IntegrationProvider
{
    Google = 0,
    Microsoft = 1,
    Slack = 2,
    Discord = 3,
    GitHub = 4,
    Autodesk = 5,
    CustomWebhook = 6,
    Other = 7
}

public enum IntegrationAccountStatus
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Error = 3,
    Deleted = 4
}

public enum WebhookEndpointStatus
{
    Active = 0,
    Disabled = 1,
    Error = 2,
    Deleted = 3
}

public enum InvitationMode
{
    AdminOnly = 0,
    DomainRestricted = 1,
    InviteLink = 2,
    Closed = 3
}

public enum PlanStatus
{
    Active = 0,
    Archived = 1,
    InternalOnly = 2
}

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Suspended = 3,
    Cancelled = 4,
    Expired = 5
}

public enum AttachmentOwnerType
{
    Message = 0,
    Post = 1,
    TaskItem = 2,
    ArtifactVersion = 3,
    Comment = 4,
    ActivityLog = 5,
    Workspace = 6
}

public enum ArtifactType
{
    Document = 0,
    Image = 1,
    Video = 2,
    Code = 3,
    Presentation = 4,
    Spreadsheet = 5,
    Archive = 6,
    Other = 7
}

public enum ArtifactStatus
{
    Draft = 0,
    Submitted = 1,
    Reviewed = 2,
    Approved = 3,
    Archived = 4
}

public enum DockArea
{
    Left = 0,
    Right = 1,
    Bottom = 2,
    Center = 3
}

public enum RadialMenuScope
{
    Global = 0,
    Workspace = 1,
    Project = 2
}

public enum LayoutScopeType
{
    Global = 0,
    Workspace = 1,
    Group = 2,
    Project = 3
}

public enum CommandActionType
{
    Navigate = 0,
    OpenModal = 1,
    ApiCall = 2,
    ClientAction = 3
}

public enum CommandContextType
{
    Global = 0,
    Workspace = 1,
    Group = 2,
    Project = 3,
    TaskItem = 4,
    Artifact = 5,
    Message = 6
}

public enum RadialMenuDirection
{
    Up = 0,
    UpRight = 1,
    Right = 2,
    DownRight = 3,
    Down = 4,
    DownLeft = 5,
    Left = 6,
    UpLeft = 7,
    Center = 8
}
