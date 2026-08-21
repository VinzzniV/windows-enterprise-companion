import type { SemanticStatus } from '../../shared/ui/SemanticStatusBadge';

export interface NessusScanStatusPresentation {
  status: SemanticStatus;
  context: string | null;
  technicalDetail: string | null;
}

export function nessusScanStatus(rawStatus: string | null, rawError: string | null): NessusScanStatusPresentation {
  const providerStatus = rawStatus?.trim() || null;
  const error = rawError?.trim() || null;
  const providerDetail = providerStatus ? `Provider status: ${providerStatus}` : null;

  if (error) {
    return {
      status: { dimension: 'execution', value: 'failed' },
      context: 'Scan status',
      technicalDetail: [error, providerDetail].filter(Boolean).join('\n'),
    };
  }

  const result = (status: SemanticStatus, context: string | null): NessusScanStatusPresentation => ({
    status,
    context,
    technicalDetail: providerDetail,
  });

  switch (providerStatus?.toLowerCase()) {
    case 'aborted':
      return result({ dimension: 'execution', value: 'failed' }, 'Aborted · Partial results possible');
    case 'canceled':
      return result({ dimension: 'execution', value: 'partial' }, 'Canceled');
    case 'completed':
      return result({ dimension: 'execution', value: 'succeeded' }, null);
    case 'empty':
      return result({ dimension: 'availability', value: 'missing' }, 'Never run');
    case 'imported':
      return result({ dimension: 'availability', value: 'available' }, 'Imported scan');
    case 'initializing':
      return result({ dimension: 'execution', value: 'running' }, 'Initializing');
    case 'pausing':
      return result({ dimension: 'execution', value: 'running' }, 'Pausing');
    case 'paused':
      return result({ dimension: 'lifecycle', value: 'pending' }, 'Paused');
    case 'pending':
      return result({ dimension: 'lifecycle', value: 'pending' }, 'Waiting for scanner');
    case 'processing':
      return result({ dimension: 'execution', value: 'running' }, 'Processing results');
    case 'publishing':
      return result({ dimension: 'execution', value: 'running' }, 'Publishing results');
    case 'resuming':
      return result({ dimension: 'execution', value: 'running' }, 'Resuming');
    case 'running':
      return result({ dimension: 'execution', value: 'running' }, 'Scan');
    case 'stopped':
    case 'stopping':
      return result({ dimension: 'execution', value: 'running' }, 'Stopping');
    case undefined:
      return result({ dimension: 'availability', value: 'unknown' }, 'Status unavailable');
    default:
      return result({ dimension: 'availability', value: 'unknown' }, 'Unmapped scan status');
  }
}
