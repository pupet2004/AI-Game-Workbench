using Workbench.Core.Projects;

namespace Workbench.Project.Detection;

public sealed record ProjectDetectionResult(string RootPath, string SuggestedName, ProjectType ProjectType);
