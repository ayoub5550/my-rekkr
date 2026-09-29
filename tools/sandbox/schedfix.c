/* gVisor sandbox: sched_get_priority_min/max(SCHED_FIFO) both return 0, so glibc's
 * pthread_attr_setschedparam() rejects every realtime priority with EINVAL. FMOD (Unity audio,
 * FSBTool) treats that as fatal: "Unable to initialize any audio device (even nosound)".
 * Preload this to make the realtime-priority requests no-ops (threads then run as normal). */
#define _GNU_SOURCE
#include <pthread.h>
#include <sched.h>
int pthread_attr_setschedparam(pthread_attr_t *a, const struct sched_param *p) { (void)a; (void)p; return 0; }
int pthread_attr_setschedpolicy(pthread_attr_t *a, int policy) { (void)a; (void)policy; return 0; }
int pthread_setschedparam(pthread_t t, int policy, const struct sched_param *p) { (void)t; (void)policy; (void)p; return 0; }
int sched_setscheduler(pid_t pid, int policy, const struct sched_param *p) { (void)pid; (void)policy; (void)p; return 0; }
int sched_get_priority_max(int policy) { return (policy == SCHED_FIFO || policy == SCHED_RR) ? 99 : 0; }
int sched_get_priority_min(int policy) { return (policy == SCHED_FIFO || policy == SCHED_RR) ? 1 : 0; }
