import { Loader2, LockKeyhole } from "lucide-react";
import { Button } from "@/components/ui/button";
import { formatBaht } from "@/lib/format";

/** The one primary action of the cart: the amount is on the button, so there is nothing left to look for. */
export function CheckoutButton({ total, busy, onCheckout }: { total: number; busy: boolean; onCheckout: () => void }) {
  return (
    <Button size="lg" className="h-13 w-full rounded-xl text-base font-bold shadow-md shadow-primary/30"
      disabled={busy} onClick={onCheckout} aria-label={`ชำระเงิน ${formatBaht(total)}`}>
      {busy ? <Loader2 aria-hidden className="animate-spin" /> : <LockKeyhole aria-hidden />}
      ชำระเงิน {formatBaht(total)}
    </Button>
  );
}
