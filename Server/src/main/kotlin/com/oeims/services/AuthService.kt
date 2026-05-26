package com.oeims.services

import com.auth0.jwt.JWT
import com.auth0.jwt.algorithms.Algorithm
import com.oeims.models.dto.AuthResponse
import com.oeims.exceptions.ConflictException
import com.oeims.exceptions.UnauthorizedException
import com.oeims.exceptions.ValidationException
import com.oeims.models.Email
import com.oeims.models.Password
import com.oeims.models.ids.UserId
import com.oeims.models.UserRole
import com.oeims.models.ids.toUserId
import com.oeims.repositories.interfaces.IUserRepository
import org.mindrot.jbcrypt.BCrypt
import java.util.Date

class AuthService(
    private val userRepository: IUserRepository,
    private val jwtConfig: JwtConfig
) {

    suspend fun register(email: Email, password: Password, role: UserRole): AuthResponse {
        if (userRepository.existsByEmail(email.address))
            throw ConflictException("Email already registered")

        val userRole = runCatching { UserRole.valueOf(role.name.uppercase()) }
            .getOrElse { throw ValidationException("Invalid role: $role") }

        val hash = BCrypt.hashpw(password.value, BCrypt.gensalt())
        val user = userRepository.create(email.address, userRole, hash)

        return AuthResponse(
            token = issueToken(user.id.toUserId(), user.role),
            userId = user.id.toString(),
            email = user.email,
            role = user.role
        )
    }

    suspend fun login(email: Email, password: Password): AuthResponse {
        val user = userRepository.findByEmail(email.address)
            ?: throw UnauthorizedException("Invalid credentials")

        if (!BCrypt.checkpw(password.value, user.passwordHash))
            throw UnauthorizedException("Invalid credentials")

        return AuthResponse(
            token = issueToken(user.id.toUserId(), user.role),
            userId = user.id.toString(),
            email = user.email,
            role = user.role
        )
    }

    private fun issueToken(userId: UserId, role: UserRole): String =
        JWT.create()
            .withIssuer(jwtConfig.issuer)
            .withAudience(jwtConfig.audience)
            .withClaim("userId", userId.value.toString())
            .withClaim("role", role.name)
            .withExpiresAt(Date(System.currentTimeMillis() + jwtConfig.expirationMs))
            .sign(Algorithm.HMAC256(jwtConfig.secret))
}
