import type {
  DirectoryUserAccountStateFilter,
  DirectoryUserSortDirection,
  DirectoryUserSortField,
  UserDirectoryConnectionRequest,
} from '../../shared/api-types';
import type { CredentialValues } from '../../shared/targets/Credentials';

export interface UserDirectoryEndpoint {
  domain: string;
  server: string;
}

export interface UserFilters {
  search: string;
  department: string;
  baseDistinguishedName: string;
  accountState: DirectoryUserAccountStateFilter;
}

export interface UserSort {
  field: DirectoryUserSortField;
  direction: DirectoryUserSortDirection;
}

export const emptyUserDirectoryEndpoint: UserDirectoryEndpoint = { domain: '', server: '' };

export const emptyUserFilters: UserFilters = {
  search: '',
  department: '',
  baseDistinguishedName: '',
  accountState: 'ALL',
};

export const defaultUserSort: UserSort = {
  field: 'DISPLAY_NAME',
  direction: 'ASCENDING',
};

export const userDirectoryViewKey = 'users.directory';

export function toUserDirectoryConnection(
  endpoint: UserDirectoryEndpoint,
  adminCredentials: CredentialValues | null,
): UserDirectoryConnectionRequest | null {
  const request: UserDirectoryConnectionRequest = {};
  if (endpoint.domain.trim() !== '') request.domain = endpoint.domain.trim();
  if (endpoint.server.trim() !== '') request.server = endpoint.server.trim();
  if (adminCredentials?.userName.trim()) {
    request.userName = adminCredentials.userName.trim();
    request.userDomain = adminCredentials.domain.trim() || null;
    request.password = adminCredentials.password;
  }
  return Object.keys(request).length > 0 ? request : null;
}

export function formatDirectoryTimestamp(value: string | null): string {
  return value ? new Date(value).toLocaleString() : 'Not available';
}
