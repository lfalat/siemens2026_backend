import { createContext, use } from 'react'
import type { StaffAuth } from './staffAuth.ts'

export const StaffAuthContext = createContext<StaffAuth | null>(null)

export function useStaffAuth(): StaffAuth {
  const auth = use(StaffAuthContext)
  if (!auth) throw new Error('useStaffAuth must be used inside the /staff area')
  return auth
}
