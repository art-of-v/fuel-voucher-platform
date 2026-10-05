import { useState } from "react";
import { Input } from "@/components/ui/input";

interface DecimalSettingInputProps
  extends Omit<React.ComponentProps<"input">, "value" | "onChange"> {
  /** The committed number. */
  value: number;
  /** Called on blur with the parsed value, or 0 when the field was cleared or unparseable. */
  onCommit: (value: number) => void;
}

/**
 * A number field a settings screen can actually edit.
 *
 * The obvious implementation — `value={number}` plus `onChange={e => onCommit(parseFloat(e.target.value) || 0)}`
 * — makes the field impossible to clear. Deleting the last character yields `""`, `parseFloat("")` is `NaN`,
 * `|| 0` turns it into `0`, and the field snaps back to a digit before the replacement can be typed. Changing
 * 5 to 3 then means typing through 0, which is the "I have to delete the zero by hand" behaviour this
 * component exists to remove.
 *
 * So editing runs on a draft string that starts empty on focus, which means the first keystroke replaces
 * rather than appends — typing 3 over a 5 gives 3, not 53, without relying on select-all behaviour that a
 * test cannot observe. A `dirty` flag keeps a focus-and-tab from committing, so merely tabbing through the
 * form does not rewrite every tier it displayed.
 *
 * A comma is accepted as the decimal separator, because the admin panel is used in Ukrainian and German
 * locales where the keyboard offers it, and `parseFloat("0,5")` would otherwise silently become 0.
 */
export function DecimalSettingInput({ value, onCommit, onFocus, onBlur, ...inputProps }: DecimalSettingInputProps) {
  // draft === null means "not editing": the field mirrors the committed value.
  const [draft, setDraft] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);

  return (
    <Input
      {...inputProps}
      value={draft ?? String(value)}
      onFocus={(e) => {
        setDraft("");
        setDirty(false);
        onFocus?.(e);
      }}
      onChange={(e) => {
        setDraft(e.target.value);
        setDirty(true);
      }}
      onBlur={(e) => {
        if (dirty) {
          const parsed = parseFloat((draft ?? "").replace(",", "."));
          onCommit(Number.isFinite(parsed) && parsed >= 0 ? parsed : 0);
        }
        setDraft(null);
        setDirty(false);
        onBlur?.(e);
      }}
    />
  );
}
