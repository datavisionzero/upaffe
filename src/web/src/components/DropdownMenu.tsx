import { Menu as MenuPrimitive } from "@base-ui/react/menu";
import { Check } from "lucide-react";

export function DropdownMenu(props: MenuPrimitive.Root.Props) {
  return <MenuPrimitive.Root data-slot="dropdown-menu" {...props} />;
}

export function DropdownMenuTrigger(props: MenuPrimitive.Trigger.Props) {
  return <MenuPrimitive.Trigger data-slot="dropdown-menu-trigger" {...props} />;
}

export function DropdownMenuContent({
  align = "start",
  alignOffset = 0,
  side = "bottom",
  sideOffset = 4,
  className = "",
  ...props
}: MenuPrimitive.Popup.Props &
  Pick<MenuPrimitive.Positioner.Props, "align" | "alignOffset" | "side" | "sideOffset">) {
  return (
    <MenuPrimitive.Portal>
      <MenuPrimitive.Positioner
        align={align}
        alignOffset={alignOffset}
        className="ui-menu-positioner"
        side={side}
        sideOffset={sideOffset}
      >
        <MenuPrimitive.Popup
          className={`ui-menu ${className}`}
          data-slot="dropdown-menu-content"
          {...props}
        />
      </MenuPrimitive.Positioner>
    </MenuPrimitive.Portal>
  );
}

export function DropdownMenuGroup(props: MenuPrimitive.Group.Props) {
  return <MenuPrimitive.Group data-slot="dropdown-menu-group" {...props} />;
}

export function DropdownMenuLabel({ className = "", ...props }: MenuPrimitive.GroupLabel.Props) {
  return <MenuPrimitive.GroupLabel className={`ui-menu-label ${className}`} data-slot="dropdown-menu-label" {...props} />;
}

export function DropdownMenuItem({ className = "", destructive = false, ...props }:
  MenuPrimitive.Item.Props & { destructive?: boolean }) {
  return <MenuPrimitive.Item className={`ui-menu-item ${className}`} data-destructive={destructive || undefined}
    data-slot="dropdown-menu-item" {...props} />;
}

export function DropdownMenuRadioGroup(props: MenuPrimitive.RadioGroup.Props) {
  return <MenuPrimitive.RadioGroup data-slot="dropdown-menu-radio-group" {...props} />;
}

export function DropdownMenuRadioItem({ className = "", children, ...props }: MenuPrimitive.RadioItem.Props) {
  return (
    <MenuPrimitive.RadioItem className={`ui-menu-item ui-menu-radio ${className}`}
      data-slot="dropdown-menu-radio-item" {...props}>
      {children}
      <MenuPrimitive.RadioItemIndicator className="ui-menu-indicator">
        <Check aria-hidden="true" size={14} />
      </MenuPrimitive.RadioItemIndicator>
    </MenuPrimitive.RadioItem>
  );
}

export function DropdownMenuSeparator({ className = "", ...props }: MenuPrimitive.Separator.Props) {
  return <MenuPrimitive.Separator className={`ui-menu-separator ${className}`}
    data-slot="dropdown-menu-separator" {...props} />;
}
