using Workbench.Core.Layout;
using Workbench.Core.Projects;
using Workbench.Project.Git;

namespace Workbench.Project.Opening;

public sealed record ProjectOpenResult(Workbench.Core.Projects.Project Project, ProjectLayout Layout, GitSnapshot Git);
