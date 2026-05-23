package com.oeims.services

import com.oeims.models.dto.ParticipantStatusUpdate
import com.oeims.repositories.interfaces.IParticipantRepository
import com.oeims.websocket.IConnectionRegistry
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.slf4j.LoggerFactory
import java.time.Instant
import kotlin.time.Duration.Companion.milliseconds

class HeartbeatService(
    private val participantRepository: IParticipantRepository,
    private val connectionRegistry: IConnectionRegistry,
    private val config: HeartbeatConfig
) {
    private val logger = LoggerFactory.getLogger(HeartbeatService::class.java)

    fun start(scope: CoroutineScope) {
        scope.launch {
            while (true) {
                delay(config.intervalMs.milliseconds)
                try {
                    checkHeartbeats()
                } catch (e: Throwable) {
                    logger.warn("Heartbeat sweep failed", e)
                }
            }
        }
    }

    private suspend fun checkHeartbeats() {
        val threshold = Instant.now().minusMillis(config.timeoutMs)
        val timedOut = participantRepository.markTimedOut(threshold)

        timedOut.forEach { participant ->
            connectionRegistry.broadcastStatusUpdate(
                sessionId = participant.sessionId,
                update    = ParticipantStatusUpdate(
                    participantId    = participant.id.toString(),
                    connectionStatus = "TIMED_OUT"
                )
            )
        }
    }
}
