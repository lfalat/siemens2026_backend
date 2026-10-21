import { createBrowserRouter } from 'react-router'
import { PublicLayout } from '../layout/PublicLayout.tsx'
import { AccessDeniedPage } from './Error/AccessDeniedPage.tsx'
import { NotFoundPage } from './Error/NotFoundPage.tsx'
import { HomePage } from './PublicUser/HomePage.tsx'
import { LoginPage } from './PublicUser/LoginPage.tsx'
import { JobOfferDetailPage } from './PublicUser/JobOfferDetailPage.tsx'
import { JobOffersPage } from './PublicUser/JobOffersPage.tsx'

// Two route trees (ADR-F004). Everything under /staff is lazy: applicants never download staff code or MSAL.
export const router = createBrowserRouter([
  {
    path: '/',
    Component: PublicLayout,
    children: [{ index: true, Component: HomePage }],
  },
  {
    path: '/aktualne-pozicie',
    Component: PublicLayout,
    children: [{ index: true, Component: JobOffersPage }],
  },
  {
    path: '/aktualne-pozicie/:offerId',
    Component: PublicLayout,
    children: [{ index: true, Component: JobOfferDetailPage }],
  },
  { path: '/login', Component: LoginPage },
  { path: '/access-denied', Component: AccessDeniedPage },
  {
    path: '/staff',
    lazy: () =>
      import('./Staff/staffRoute.tsx').then((m) => m.loadStaffRoute()),
    children: [
      {
        index: true,
        lazy: () =>
          import('./Staff/StaffHomePage.tsx').then((m) => ({
            Component: m.StaffHomePage,
          })),
      },
      {
        path: 'access-denied',
        lazy: () =>
          import('./Error/AccessDeniedPage.tsx').then((m) => ({
            Component: m.StaffAccessDeniedPage,
          })),
      },
      {
        path: 'jobs',
        lazy: () =>
          import('./Staff/StaffJobsPage.tsx').then((m) => ({
            Component: m.StaffJobsPage,
          })),
      },
    ],
  },
  { path: '*', Component: NotFoundPage },
])
