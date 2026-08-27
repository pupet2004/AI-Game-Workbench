import DOMPurify from "dompurify"
import { marked } from "marked"
import { createEffect, createSignal, on } from "solid-js"
import { project, type Projection } from "./kilo-markdown-stream"

type WorkbenchMarkdownProps = { text: string; live?: boolean }

function source(projection: Projection) {
  return projection.blocks.map((block) => {
    if (block.mode !== "code") return block.src
    const language = block.language ?? "text"
    return `\n\n\`\`\`${language}\n${block.src}\n\`\`\`\n\n`
  }).join("\n")
}

function sanitize(html: string) {
  return DOMPurify.sanitize(html, { USE_PROFILES: { html: true }, FORBID_TAGS: ["style", "script"], FORBID_CONTENTS: ["style", "script"], ADD_ATTR: ["target", "rel"] })
}

function decorate(root: HTMLDivElement) {
  root.querySelectorAll("pre").forEach((pre) => {
    if (pre.parentElement?.dataset.component === "markdown-code") return
    const wrapper = document.createElement("div")
    wrapper.dataset.component = "markdown-code"
    const button = document.createElement("button")
    button.type = "button"
    button.dataset.component = "icon-button"
    button.dataset.variant = "secondary"
    button.dataset.size = "small"
    button.dataset.slot = "markdown-copy-button"
    button.textContent = "Copy"
    button.addEventListener("click", async () => {
      await navigator.clipboard?.writeText(pre.textContent ?? "")
      button.textContent = "Copied"
      setTimeout(() => { button.textContent = "Copy" }, 900)
    })
    pre.replaceWith(wrapper)
    wrapper.append(pre, button)
  })
}

export function WorkbenchMarkdown(props: WorkbenchMarkdownProps) {
  let root: HTMLDivElement | undefined
  const [projection, setProjection] = createSignal<Projection>()
  createEffect(on(() => [props.text, props.live ?? false] as const, ([text, live]) => setProjection((previous) => project(previous, text, live)), { defer: false }))
  createEffect(() => {
    const current = projection()
    if (!root || !current) return
    root.innerHTML = sanitize(marked.parse(source(current)) as string)
    decorate(root)
  })
  return <div ref={root} class="workbench-markdown" data-component="markdown" />
}
