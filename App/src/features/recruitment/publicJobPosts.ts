import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ApiError, request } from '../../shared/api/http.ts'
import type { EmploymentType, PaymentType } from './vocabulary.ts'

// The public job post reads. Anonymous, so nothing here may import the staff auth (ADR-F004).
// TODO: replace the hand-written types with the generated OpenAPI client (ADR-F005).

/** A published job post as `GET /api/v1/recruitment/job-posts` lists it. */
export interface PublishedJobPostItem {
  id: string
  title: string
  location: string
  department: string
  employmentType: EmploymentType
  /** EUR, per hour, month or year as `paymentType` says. */
  salary: number
  paymentType: PaymentType
  createdAt: string
}

/** One page of the published job posts. */
export interface PublishedJobPostPage {
  items: PublishedJobPostItem[]
  /** How many posts match across all pages. */
  total: number
  offset: number
  limit: number
}

/** A published job post as `GET /api/v1/recruitment/job-posts/{id}` returns it. */
export interface PublishedJobPost extends PublishedJobPostItem {
  description: string
  openPositions: number
}

export interface PublishedJobPostFilter {
  offset: number
  limit: number
  search?: string
  employmentType?: EmploymentType
}

/** One page of the published job posts, filtered on the server. */
export function usePublishedJobPosts(filter: PublishedJobPostFilter) {
  return useQuery({
    queryKey: ['recruitment', 'job-posts', 'published', filter],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({
        offset: String(filter.offset),
        limit: String(filter.limit),
      })
      if (filter.search) params.set('search', filter.search)
      if (filter.employmentType)
        params.set('employmentType', filter.employmentType)
      return request<PublishedJobPostPage>(
        `/api/v1/recruitment/job-posts?${params}`,
        { signal },
      )
    },
    // Keeps the current page on screen while the next one loads, instead of flashing an empty table.
    placeholderData: keepPreviousData,
  })
}

/** One published job post; a draft or a missing post answers 404. */
export function usePublishedJobPost(id: string) {
  return useQuery({
    queryKey: ['recruitment', 'job-posts', 'published', id],
    queryFn: ({ signal }) =>
      request<PublishedJobPost>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(id)}`,
        { signal },
      ),
    // A 404 will not turn into a post on a retry.
    retry: (failureCount, error) =>
      !(error instanceof ApiError && error.status === 404) && failureCount < 3,
  })
}
