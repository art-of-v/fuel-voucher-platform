import { useEffect, useState } from "react";
import { Input } from "@/components/ui/input";

interface DateInputProps {
  value: string;
  onChange: (iso: string) => void;
  className?: string;
  title?: string;
}

export function dateToISO(display: string): string {
  const digits = display.replace(/\D/g, "");
  if (digits.length !== 8) return "";
  return `${digits.slice(4, 8)}-${digits.slice(2, 4)}-${digits.slice(0, 2)}`;
}

export function isoToDisplay(iso: string): string {
  if (!iso) return "";
  const m = iso.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (!m) return "";
  return `${m[3]}.${m[2]}.${m[1]}`;
}

export default function DateInput({ value, onChange, className, title }: DateInputProps) {
  const [text, setText] = useState(isoToDisplay(value));

  useEffect(() => {
    setText(isoToDisplay(value));
  }, [value]);

  return (
    <Input
      type="text"
      inputMode="numeric"
      placeholder="dd.mm.yyyy"
      value={text}
      title={title}
      onChange={(e) => {
        const digits = e.target.value.replace(/\D/g, "").slice(0, 8);
        let out = digits;
        if (digits.length > 4) out = `${digits.slice(0, 2)}.${digits.slice(2, 4)}.${digits.slice(4)}`;
        else if (digits.length > 2) out = `${digits.slice(0, 2)}.${digits.slice(2)}`;
        setText(out);
        onChange(dateToISO(digits));
      }}
      className={className}
    />
  );
}
