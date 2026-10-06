import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import { OrderView } from "@/components/order-view";

export const metadata: Metadata = { title: "ออเดอร์ของคุณ | TNC Store" };

export default async function OrderPage({ params }: PageProps<"/orders/[id]">) {
  await connection();
  const { id } = await params;
  return (
    <>
      <header className="border-b">
        <div className="mx-auto w-full max-w-xl px-3 py-3 sm:px-6">
          <Link href="/" className="text-xl font-bold tracking-tight">TNC Store</Link>
        </div>
      </header>
      <main className="mx-auto w-full max-w-xl flex-1 px-3 py-6 sm:px-6">
        {/* The order is loaded in the browser so the guest cookie decides who may see it. */}
        <OrderView id={id} />
      </main>
    </>
  );
}
