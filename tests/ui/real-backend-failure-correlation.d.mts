export interface SmokeFailedApiResponseLike {
  method: string;
  path: string;
  status: number;
}

export interface FailureCorrelationEvidence<TFailure extends SmokeFailedApiResponseLike = SmokeFailedApiResponseLike> {
  failedApiResponses: readonly TFailure[];
  consoleErrors: readonly string[];
}

export function isExpectedFailure(failure: SmokeFailedApiResponseLike): boolean;

export function selectPr03cExpectedRevocationRefreshFailures<TFailure extends SmokeFailedApiResponseLike>(
  evidence: Pick<FailureCorrelationEvidence<TFailure>, 'failedApiResponses'>,
  postRevocationFailureStart: number,
  taskId: string,
  projectId: string,
): TFailure[];

export function classifyUnexpectedApiFailures<TFailure extends SmokeFailedApiResponseLike>(
  evidence: Pick<FailureCorrelationEvidence<TFailure>, 'failedApiResponses'>,
  scenarioExpectedFailures?: readonly TFailure[],
): {
  unexpected: TFailure[];
  remainingScenarioExpected: TFailure[];
};

export function classifyUnexpectedConsoleErrors<TFailure extends SmokeFailedApiResponseLike>(
  evidence: FailureCorrelationEvidence<TFailure>,
  scenarioExpectedFailures?: readonly TFailure[],
): string[];
