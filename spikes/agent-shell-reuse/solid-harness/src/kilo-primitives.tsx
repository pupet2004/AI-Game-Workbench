import { Button as KobalteButton } from "@kobalte/core/button"
import type { ParentProps } from "solid-js"

type KiloButtonProps = ParentProps<{
  variant?: "primary" | "secondary" | "ghost"
  size?: "small" | "normal" | "large"
  disabled?: boolean
  type?: "button" | "submit" | "reset"
  class?: string
  onClick?: () => void
}>

/** Small compatibility cut based on Kilo's button data attributes and Kobalte boundary. */
export function KiloButton(props: KiloButtonProps) {
  return (
    <KobalteButton
      class={`kilo-button ${props.class ?? ""}`}
      data-component="button"
      data-size={props.size ?? "normal"}
      data-variant={props.variant ?? "secondary"}
      disabled={props.disabled}
      onClick={props.onClick}
      type={props.type ?? "button"}
    >
      {props.children}
    </KobalteButton>
  )
}
