package com.oeims.models.dto

import com.oeims.models.UserRole
import kotlinx.serialization.Serializable

@Serializable
data class LoginRequest(
    val email: String,
    val password: String
)

@Serializable
data class RegisterRequest(
    val email: String,
    val password: String,
    val role: UserRole   // "STUDENT" | "PROFESSOR"
)

@Serializable
data class AuthResponse(
    val token: String,
    val userId: String,
    val email: String,
    val role: UserRole
)
