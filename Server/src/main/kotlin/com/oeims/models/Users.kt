package com.oeims.models

import kotlinx.serialization.Serializable
import org.jetbrains.exposed.dao.id.UUIDTable
import org.jetbrains.exposed.sql.javatime.timestamp

@Serializable
enum class UserRole { STUDENT, PROFESSOR }

object Users : UUIDTable("users") {
    val email        = varchar("email", 254).uniqueIndex()
    val role         = enumerationByName("role", 16, UserRole::class)
    val passwordHash = varchar("password_hash", 60)
    val createdAt    = timestamp("created_at")
}
