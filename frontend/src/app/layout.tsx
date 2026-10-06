import type { Metadata } from "next";
import { Noto_Sans_Thai } from "next/font/google";
import { Toaster } from "@/components/ui/sonner";
import "./globals.css";

const font = Noto_Sans_Thai({ variable: "--font-sans", subsets: ["thai", "latin"] });

export const metadata: Metadata = {
  title: "TNC Store",
  description: "อุปกรณ์ไอทีและแกดเจ็ต สั่งง่าย จ่ายไว",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="th" className={`${font.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col">
        {children}
        <Toaster position="top-center" richColors />
      </body>
    </html>
  );
}
