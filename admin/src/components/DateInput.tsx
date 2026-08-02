import { useEffect, useRef, useState } from "react";
import { Calendar } from "lucide-react";
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
  const nativeRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setText(isoToDisplay(value));
  }, [value]);

  const openPicker = () => {
    const el = nativeRef.current;
    if (!el) return;
    if (typeof el.showPicker === "function") {
      try {
        el.showPicker();
        return;
      } catch {
        // fall through to focus/click fallback
      }
    }
    el.focus();
    el.click();
  };

  return (
    <div className={`relative inline-flex items-center border rounded-md ${className ?? ""}`}>
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
        className="h-full w-full border-0 bg-transparent px-3 py-1 pr-9 text-base shadow-none focus-visible:ring-0 focus-visible:outline-none md:text-sm"
      />
      <button
        type="button"
        onClick={openPicker}
        title={title}
        tabIndex={-1}
        aria-label={title}
        className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-white"
      >
        <Calendar className="w-4 h-4" />
      </button>
      <input
        ref={nativeRef}
        type="date"
        value={dateToISO(text) || value}
        onChange={(e) => {
          if (e.target.value) {
            onChange(e.target.value);
            setText(isoToDisplay(e.target.value));
          }
        }}
        className="fixed w-px h-px opacity-0 pointer-events-none"
        tabIndex={-1}
        aria-hidden="true"
      />
    </div>
  );
}
